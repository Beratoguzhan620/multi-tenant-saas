using System.Security.Claims;
using System.Security.Cryptography;
using Helpdesk.Api.Common;
using Helpdesk.Api.Features.Audit;
using Helpdesk.Api.Features.Auth;
using Helpdesk.Api.Features.Plans;
using Helpdesk.Core;
using Helpdesk.Core.Audit;
using Helpdesk.Core.Tenants;
using Helpdesk.Core.Users;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Invitations;

public sealed record InviteRequest(string Email, Role Role);

public sealed record InvitationResponse(Guid Id, string Email, Role Role, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Expired);

/// <summary>The token is shown once, to the admin who sends it on; only its hash is kept.</summary>
public sealed record InvitationCreated(InvitationResponse Invitation, string Token);

public sealed record TokenRequest(string Token);

public sealed record InvitationPreview(string OrganizationName, string Email, Role Role, bool Expired, bool HasAccount);

public sealed record JoinRequest(string Token, string DisplayName, string Password);

/// <summary>
/// Inviting people into an organization. A token carries the organization's id next to its secret, so the
/// invitation is found under that organization's isolation like every other tenant row, never by searching
/// across organizations.
/// </summary>
internal static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/invitations").WithTags("Invitations").RequireAuthorization(AuthSetup.AdminPolicy);
        admin.MapGet("/", List).WithSummary("Open invitations (admin)");
        admin.MapPost("/", Invite).WithSummary("Invite someone (admin)")
            .WithDescription("Takes a seat until accepted, revoked or expired (7 days). Inviting the same address again replaces the old link.");
        admin.MapDelete("/{id:guid}", Revoke).WithSummary("Revoke an invitation (admin)");

        var open = app.MapGroup("/api/invitations").WithTags("Invitations");
        open.MapPost("/preview", Preview).AllowAnonymous().RequireRateLimiting(AuthEndpoints.RateLimit)
            .WithSummary("What an invitation is for").WithDescription("Lets the join page show the organization and whether to sign in or create an account.");
        open.MapPost("/accept", Accept).RequireAuthorization().RequireRateLimiting(AuthEndpoints.RateLimit)
            .WithSummary("Accept with the account you are signed in with").WithDescription("The account's email must be the invited one. Returns a session for the organization.");
        app.MapPost("/api/auth/join", Join).WithTags("Invitations").AllowAnonymous().RequireRateLimiting(AuthEndpoints.RateLimit)
            .WithSummary("Create an account from an invitation").WithDescription("For people without an account; the email is the invited one.");
        return app;
    }

    private static async Task<Ok<List<InvitationResponse>>> List(HelpdeskDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var invitations = await db.Invitations.AsNoTracking().OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
        return TypedResults.Ok(invitations.ConvertAll(i => Response(i, now)));
    }

    private static async Task<Created<InvitationCreated>> Invite(
        InviteRequest request, HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tenantId = tenant.RequiredTenantId;
        var email = User.NormalizeEmail(request.Email ?? string.Empty);
        var secret = RandomNumberGenerator.GetBytes(32);
        var token = Secrets.Encode([.. tenantId.ToByteArray(), .. secret]);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await (from m in db.Memberships join u in db.Users on m.UserId equals u.Id where m.TenantId == tenantId && u.Email == email select m).AnyAsync(ct))
        {
            throw new DomainException(ErrorCodes.InvalidState, "This person is already a member.");
        }

        // A new link for the same address replaces the old one and keeps its seat.
        var previous = await db.Invitations.SingleOrDefaultAsync(i => i.Email == email, ct);
        if (previous is not null)
        {
            db.Invitations.Remove(previous);
            await db.SaveChangesAsync(ct);
        }

        await Seats.TakeAsync(db, tenantId, now, ct);
        var invitation = Invitation.Create(tenantId, email, request.Role, Secrets.Hash(token), tenant.UserId!.Value, tenant.Role!.Value, now);
        db.Invitations.Add(invitation);
        await AuditLog.AddAsync(db, tenant, AuditActions.MemberInvited, invitation.Email, $"as {invitation.Role}", now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Created("/api/invitations", new InvitationCreated(Response(invitation, now), token));
    }

    private static async Task<NoContent> Revoke(Guid id, HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no such invitation.");
        db.Invitations.Remove(invitation);
        await AuditLog.AddAsync(db, tenant, AuditActions.InvitationRevoked, invitation.Email, null, clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<InvitationPreview>> Preview(
        TokenRequest request, HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var (invitation, organization) = await FindAsync(request.Token, db, tenant, ct);
        var hasAccount = await db.Users.AnyAsync(u => u.Email == invitation.Email, ct);
        return TypedResults.Ok(new InvitationPreview(organization.Name, invitation.Email, invitation.Role, invitation.IsExpired(clock.GetUtcNow()), hasAccount));
    }

    private static async Task<Ok<SessionResponse>> Accept(
        TokenRequest request, ClaimsPrincipal principal, HelpdeskDbContext db, TenantContext tenant, TokenService tokens,
        TimeProvider clock, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == principal.UserId(), ct);
        var (invitation, organization) = await FindAsync(request.Token, db, tenant, ct);
        if (await db.Memberships.AnyAsync(m => m.TenantId == organization.Id && m.UserId == user.Id, ct))
        {
            throw new DomainException(ErrorCodes.InvalidState, "You are already a member of this organization.");
        }

        await JoinAsync(db, invitation, user, clock.GetUtcNow(), ct);
        return TypedResults.Ok(await Sessions.CreateAsync(db, tokens, user, organization.Id, ct));
    }

    private static async Task<Results<Created<SessionResponse>, ValidationProblem>> Join(
        JoinRequest request, HelpdeskDbContext db, TenantContext tenant, PasswordVerifier passwords, TokenService tokens,
        TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(request.DisplayName?.Trim().Length is >= User.DisplayNameMinLength and <= User.DisplayNameMaxLength,
                "displayName", $"A display name has {User.DisplayNameMinLength} to {User.DisplayNameMaxLength} characters.")
            .Require(request.Password is { Length: >= AuthEndpoints.PasswordMinLength and <= 128 },
                "password", $"A password has {AuthEndpoints.PasswordMinLength} to 128 characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var (invitation, organization) = await FindAsync(request.Token, db, tenant, ct);
        if (await db.Users.AnyAsync(u => u.Email == invitation.Email, ct))
        {
            throw new DomainException(ErrorCodes.EmailTaken, "An account with this email already exists; sign in to accept the invitation.");
        }

        var now = clock.GetUtcNow();
        var user = User.Register(invitation.Email, request.DisplayName!, now);
        user.SetPasswordHash(passwords.Hash(user, request.Password));
        db.Users.Add(user);
        await JoinAsync(db, invitation, user, now, ct);
        return TypedResults.Created("/api/me", await Sessions.CreateAsync(db, tokens, user, organization.Id, ct));
    }

    private static async Task JoinAsync(HelpdeskDbContext db, Invitation invitation, User user, DateTimeOffset now, CancellationToken ct)
    {
        var membership = invitation.Accept(user.Id, user.Email, now);
        db.Memberships.Add(membership);
        db.Invitations.Remove(invitation);
        db.AuditEvents.Add(AuditEvent.Record(invitation.TenantId, user.Id, user.DisplayName, AuditActions.MemberJoined, user.Email, $"as {membership.Role}", now));
        await AuthEndpoints.SaveAsync(db, ct);
    }

    /// <summary>A malformed token, a revoked one and one for an unknown organization all look the same.</summary>
    private static async Task<(Invitation Invitation, Tenant Organization)> FindAsync(
        string? token, HelpdeskDbContext db, TenantContext tenant, CancellationToken ct)
    {
        var bytes = Secrets.Decode(token);
        var notFound = new DomainException(ErrorCodes.NotFound, "This invitation link is not valid. It may have been revoked or replaced.");
        if (bytes is not { Length: 48 })
        {
            throw notFound;
        }

        var tenantId = new Guid(bytes.AsSpan(0, 16));
        var organization = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, ct) ?? throw notFound;
        tenant.Set(tenantId, userId: null, role: null);
        var hash = Secrets.Hash(token!);
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.SecretHash == hash, ct) ?? throw notFound;
        return (invitation, organization);
    }

    private static InvitationResponse Response(Invitation i, DateTimeOffset now) =>
        new(i.Id, i.Email, i.Role, i.CreatedAt, i.ExpiresAt, i.IsExpired(now));
}
