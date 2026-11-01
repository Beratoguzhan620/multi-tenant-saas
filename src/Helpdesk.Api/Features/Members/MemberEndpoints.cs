using Helpdesk.Api.Features.Auth;
using Helpdesk.Core.Tenants;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Members;

public sealed record MemberResponse(Guid Id, string DisplayName, string Email, Role Role, DateTimeOffset JoinedAt);

internal static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/members", List).WithTags("Members").RequireAuthorization(AuthSetup.MemberPolicy)
            .WithSummary("People in your organization")
            .WithDescription("Owners first, then admins and agents, each by name. Used to pick an assignee.");
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
}
