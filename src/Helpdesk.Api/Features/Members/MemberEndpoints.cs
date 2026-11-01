using Helpdesk.Api.Features.Audit;
using Helpdesk.Api.Features.Auth;
using Helpdesk.Core;
using Helpdesk.Core.Audit;
using Helpdesk.Core.Tenants;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Members;

public sealed record MemberResponse(Guid Id, string DisplayName, string Email, Role Role, DateTimeOffset JoinedAt);

public sealed record ChangeRoleRequest(Role Role);

public sealed record TransferRequest(Guid UserId);

internal static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var members = app.MapGroup("/api/members").WithTags("Members");
        members.MapGet("/", List).RequireAuthorization(AuthSetup.MemberPolicy)
            .WithSummary("People in your organization")
            .WithDescription("Owners first, then admins and agents, each by name. Used to pick an assignee.");
        members.MapPatch("/{userId:guid}", ChangeRole).RequireAuthorization(AuthSetup.AdminPolicy)
            .WithSummary("Make someone an agent or an admin (admin)")
            .WithDescription("Only the owner changes admins. The owner's role changes through a transfer.");
        members.MapDelete("/{userId:guid}", Remove).RequireAuthorization(AuthSetup.MemberPolicy)
            .WithSummary("Remove a member, or leave yourself")
            .WithDescription("Admins remove agents; the owner removes anyone; anyone but the owner can leave. Their tickets become unassigned.");
        app.MapPost("/api/organization/transfer", Transfer).WithTags("Members").RequireAuthorization(AuthSetup.OwnerPolicy)
            .WithSummary("Hand the organization to another member (owner)").WithDescription("You stay as an admin.");
        return app;
    }

    private static async Task<Ok<List<MemberResponse>>> List(HelpdeskDbContext db, TenantContext tenant, CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var members = await (
                from m in db.Memberships.AsNoTracking()
                join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                where m.TenantId == tenantId
                select new MemberResponse(u.Id, u.DisplayName, u.Email, m.Role, m.JoinedAt))
            .ToListAsync(ct);

        // Roles are stored by name, so order them here rather than alphabetically in SQL.
        members.Sort((a, b) => b.Role != a.Role
            ? b.Role.CompareTo(a.Role)
            : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        return TypedResults.Ok(members);
    }

    private static async Task<Ok<MemberResponse>> ChangeRole(
        Guid userId, ChangeRoleRequest request, HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var membership = await FindAsync(db, tenant.RequiredTenantId, userId, ct);
        if (!Enum.IsDefined(request.Role))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "There is no such role.");
        }

        // An admin manages agents; changing who is an admin is the owner's call.
        if (tenant.Role != Role.Owner && (membership.Role == Role.Admin || request.Role == Role.Admin))
        {
            throw new DomainException(ErrorCodes.Forbidden, "Only the owner promotes or demotes admins.");
        }

        var before = membership.Role;
        membership.ChangeRole(request.Role);
        var email = await EmailAsync(db, userId, ct);
        await AuditLog.AddAsync(db, tenant, AuditActions.MemberRoleChanged, email, $"{before} → {membership.Role}", clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadAsync(db, tenant.RequiredTenantId, userId, ct));
    }

    private static async Task<NoContent> Remove(Guid userId, HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var membership = await FindAsync(db, tenantId, userId, ct);
        var leaving = userId == tenant.UserId;
        if (membership.Role == Role.Owner)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The owner cannot leave or be removed; hand the organization over first.");
        }

        if (!leaving && !(tenant.Role == Role.Owner || (tenant.Role == Role.Admin && membership.Role == Role.Agent)))
        {
            throw new DomainException(ErrorCodes.Forbidden, "Admins remove agents; only the owner removes admins.");
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Tickets.Where(t => t.AssigneeId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.AssigneeId, (Guid?)null), ct);
        db.Memberships.Remove(membership);
        var email = await EmailAsync(db, userId, ct);
        await AuditLog.AddAsync(db, tenant, AuditActions.MemberRemoved, email, leaving ? "left" : null, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<MemberResponse>>> Transfer(
        TransferRequest request, HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var from = await FindAsync(db, tenantId, tenant.UserId!.Value, ct);
        var to = await FindAsync(db, tenantId, request.UserId, ct);
        Membership.TransferOwnership(from, to);
        await AuditLog.AddAsync(db, tenant, AuditActions.OwnershipTransferred, await EmailAsync(db, request.UserId, ct), null, clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        return await List(db, tenant, ct);
    }

    private static async Task<Membership> FindAsync(HelpdeskDbContext db, Guid tenantId, Guid userId, CancellationToken ct) =>
        await db.Memberships.SingleOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "This person is not a member of the organization.");

    private static async Task<MemberResponse> LoadAsync(HelpdeskDbContext db, Guid tenantId, Guid userId, CancellationToken ct) =>
        await (from m in db.Memberships.AsNoTracking()
               join u in db.Users.AsNoTracking() on m.UserId equals u.Id
               where m.TenantId == tenantId && m.UserId == userId
               select new MemberResponse(u.Id, u.DisplayName, u.Email, m.Role, m.JoinedAt)).SingleAsync(ct);

    private static Task<string> EmailAsync(HelpdeskDbContext db, Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).SingleAsync(ct);
}
