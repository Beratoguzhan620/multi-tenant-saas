using Helpdesk.Core;
using Helpdesk.Core.Tenants;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Plans;

/// <summary>Seats are members plus open invitations.</summary>
internal static class Seats
{
    public static async Task<int> UsedAsync(HelpdeskDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct) =>
        await db.Memberships.CountAsync(m => m.TenantId == tenantId, ct)
        + await db.Invitations.CountAsync(i => i.ExpiresAt > now, ct);

    /// <summary>
    /// Locks the organization's row for the rest of the transaction, so two invitations sent at the same moment
    /// cannot both take the last seat, then checks there is one left. Returns the organization.
    /// </summary>
    public static async Task<Tenant> TakeAsync(HelpdeskDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var tenant = (await db.Tenants.FromSql($"SELECT * FROM tenants WHERE id = {tenantId} FOR UPDATE").ToListAsync(ct)).Single();
        var plan = Plan.For(tenant.Plan);
        if (await UsedAsync(db, tenantId, now, ct) >= plan.Seats)
        {
            throw new DomainException(ErrorCodes.PlanLimitReached,
                $"All {plan.Seats} seats of the {plan.Name} plan are taken (members and open invitations). Upgrade or remove someone first.");
        }

        return tenant;
    }
}
