using Helpdesk.Core.Tenants;
using Helpdesk.Core.Users;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Auth;

public sealed record UserResponse(Guid Id, string Email, string DisplayName);

public sealed record OrganizationSummary(Guid Id, string Name, string Slug, PlanId Plan, Role Role);

/// <summary>Everything a client needs after signing in: the token, who it is for, the active organization and the others.</summary>
public sealed record SessionResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    UserResponse User,
    OrganizationSummary? Organization,
    IReadOnlyList<OrganizationSummary> Organizations);

public sealed record MeResponse(UserResponse User, OrganizationSummary? Organization, IReadOnlyList<OrganizationSummary> Organizations);

internal static class Sessions
{
    public static async Task<List<OrganizationSummary>> OrganizationsOfAsync(HelpdeskDbContext db, Guid userId, CancellationToken ct) =>
        await (from m in db.Memberships.AsNoTracking()
               join t in db.Tenants.AsNoTracking() on m.TenantId equals t.Id
               where m.UserId == userId
               orderby m.JoinedAt
               select new OrganizationSummary(t.Id, t.Name, t.Slug, t.Plan, m.Role)).ToListAsync(ct);

    /// <summary>A session for <paramref name="user"/> in <paramref name="tenantId"/>, or in their first organization when none is given.</summary>
    public static async Task<SessionResponse> CreateAsync(HelpdeskDbContext db, TokenService tokens, User user, Guid? tenantId, CancellationToken ct)
    {
        var organizations = await OrganizationsOfAsync(db, user.Id, ct);
        var active = tenantId is { } id ? organizations.SingleOrDefault(o => o.Id == id) : organizations.FirstOrDefault();
        var (token, expiresAt) = tokens.Issue(user, active?.Id);
        return new SessionResponse(token, expiresAt, new UserResponse(user.Id, user.Email, user.DisplayName), active, organizations);
    }
}
