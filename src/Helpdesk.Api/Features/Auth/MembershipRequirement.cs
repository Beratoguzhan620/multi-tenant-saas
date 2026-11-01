using Helpdesk.Api.Common;
using Helpdesk.Core.Tenants;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Auth;

/// <summary>The caller must currently belong to the organization in their token, with at least this role.</summary>
internal sealed record MembershipRequirement(Role Minimum) : IAuthorizationRequirement;

/// <summary>
/// The token names the organization; the database decides whether the person still belongs to it and with which
/// role. Someone removed from an organization, or demoted, loses access with their next request rather than when
/// their token expires.
/// </summary>
internal sealed class MembershipHandler(HelpdeskDbContext db, TenantContext tenant) : AuthorizationHandler<MembershipRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, MembershipRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || context.User.TenantId() is not { } tenantId)
        {
            return;
        }

        var userId = context.User.UserId();
        var membership = await db.Memberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId);
        if (membership is null || !membership.Can(requirement.Minimum))
        {
            return;
        }

        tenant.Set(tenantId, userId, membership.Role);
        context.Succeed(requirement);
    }
}
