using System.Net.Mail;
using System.Security.Claims;
using Helpdesk.Api.Common;
using Helpdesk.Api.Logging;
using Helpdesk.Core;
using Helpdesk.Core.Tenants;
using Helpdesk.Core.Users;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Auth;

public sealed record SignUpRequest(string OrganizationName, string Slug, string DisplayName, string Email, string Password);

public sealed record LoginRequest(string Email, string Password, string? Organization = null);

public sealed record SwitchRequest(Guid OrganizationId);

internal static partial class AuthEndpoints
{
    public const string RateLimit = "auth";
    public const int PasswordMinLength = 10;
    private const int PasswordMaxLength = 128;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapPost("/signup", SignUp).RequireRateLimiting(RateLimit)
            .WithSummary("Create an organization and its owner's account")
            .WithDescription("The organization starts on the Free plan. `slug` is its public address, like `kargo-hizli`.");
        group.MapPost("/login", Login).RequireRateLimiting(RateLimit)
            .WithSummary("Sign in")
            .WithDescription("The session is for `organization` (its slug) when given, otherwise for the first organization the person joined.");
        group.MapPost("/switch", Switch).RequireAuthorization()
            .WithSummary("A new token for another organization you belong to");

        app.MapGet("/api/me", Me).WithTags("Auth").RequireAuthorization().WithSummary("You and your organizations");
        return app;
    }

    private static async Task<Results<Created<SessionResponse>, ValidationProblem>> SignUp(
        SignUpRequest request, HelpdeskDbContext db, PasswordVerifier passwords, TokenService tokens, TimeProvider clock,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.OrganizationName), "organizationName", "Name the organization.")
            .Require(!string.IsNullOrWhiteSpace(request.Slug), "slug", "Choose an address for the organization, like 'kargo-hizli'.")
            .Require(IsEmail(request.Email), "email", "A valid email address is required.")
            .Require(request.DisplayName?.Trim().Length is >= User.DisplayNameMinLength and <= User.DisplayNameMaxLength,
                "displayName", $"A display name has {User.DisplayNameMinLength} to {User.DisplayNameMaxLength} characters.")
            .Require(request.Password is { Length: >= PasswordMinLength and <= PasswordMaxLength },
                "password", $"A password has {PasswordMinLength} to {PasswordMaxLength} characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new DomainException(ErrorCodes.EmailTaken, "An account with this email already exists. Sign in and create the organization from there.");
        }

        var now = clock.GetUtcNow();
        var user = User.Register(email, request.DisplayName!, now);
        user.SetPasswordHash(passwords.Hash(user, request.Password));
        var (tenant, owner) = Tenant.Create(request.OrganizationName, request.Slug, user.Id, now);
        db.Users.Add(user);
        db.Tenants.Add(tenant);
        db.Memberships.Add(owner);
        await SaveAsync(db, ct);

        LogSignedUp(loggers.CreateLogger(typeof(AuthEndpoints)), tenant.Id, tenant.Slug);
        return TypedResults.Created("/api/me", await Sessions.CreateAsync(db, tokens, user, tenant.Id, ct));
    }

    private static async Task<Results<Ok<SessionResponse>, ValidationProblem>> Login(
        LoginRequest request, HelpdeskDbContext db, PasswordVerifier passwords, TokenService tokens, ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Email), "email", "Email is required.")
            .Require(!string.IsNullOrEmpty(request.Password), "password", "Password is required.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (!passwords.Verify(user, request.Password, out var rehash) || user is null)
        {
            LogFailed(loggers.CreateLogger(typeof(AuthEndpoints)), Masking.Email(email));
            throw new DomainException(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");
        }

        if (rehash is not null)
        {
            user.SetPasswordHash(rehash);
            await db.SaveChangesAsync(ct);
        }

        Guid? tenantId = null;
        if (!string.IsNullOrWhiteSpace(request.Organization))
        {
            var slug = request.Organization.Trim().ToLowerInvariant();
            tenantId = await (from m in db.Memberships
                              join t in db.Tenants on m.TenantId equals t.Id
                              where m.UserId == user.Id && t.Slug == slug
                              select (Guid?)t.Id).SingleOrDefaultAsync(ct)
                ?? throw new DomainException(ErrorCodes.NotFound, "You are not a member of this organization.");
        }

        return TypedResults.Ok(await Sessions.CreateAsync(db, tokens, user, tenantId, ct));
    }

    private static async Task<Ok<SessionResponse>> Switch(
        SwitchRequest request, ClaimsPrincipal principal, HelpdeskDbContext db, TokenService tokens, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == principal.UserId(), ct);
        var member = await db.Memberships.AnyAsync(m => m.UserId == user.Id && m.TenantId == request.OrganizationId, ct);
        if (!member)
        {
            // Not "forbidden": whether an organization with this id exists is none of the caller's business.
            throw new DomainException(ErrorCodes.NotFound, "There is no organization with this id among yours.");
        }

        return TypedResults.Ok(await Sessions.CreateAsync(db, tokens, user, request.OrganizationId, ct));
    }

    private static async Task<Ok<MeResponse>> Me(ClaimsPrincipal principal, HelpdeskDbContext db, CancellationToken ct)
    {
        var userId = principal.UserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "The account no longer exists.");
        var organizations = await Sessions.OrganizationsOfAsync(db, userId, ct);
        var current = organizations.SingleOrDefault(o => o.Id == principal.TenantId());
        return TypedResults.Ok(new MeResponse(new UserResponse(user.Id, user.Email, user.DisplayName), current, organizations));
    }

    internal static async Task SaveAsync(HelpdeskDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_tenants_slug"))
        {
            throw new DomainException(ErrorCodes.SlugTaken, "This address is taken; choose another.");
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_users_email"))
        {
            throw new DomainException(ErrorCodes.EmailTaken, "An account with this email already exists.");
        }
    }

    internal static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= User.EmailMaxLength
        && MailAddress.TryCreate(value.Trim(), out var address)
        && string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase)
        && address.Host.Contains('.', StringComparison.Ordinal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {TenantId} ({Slug}) signed up")]
    private static partial void LogSignedUp(ILogger logger, Guid tenantId, string slug);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed sign-in for {MaskedEmail}")]
    private static partial void LogFailed(ILogger logger, string maskedEmail);
}
