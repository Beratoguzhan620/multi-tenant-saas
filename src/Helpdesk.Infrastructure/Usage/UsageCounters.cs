using Helpdesk.Core.Usage;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Infrastructure.Usage;

public static class UsageCounters
{
    /// <summary>
    /// Adds <paramref name="amount"/> if the total stays within <paramref name="limit"/>, in one statement; false
    /// when it would not. Inside a transaction the row stays locked until commit, and a rollback undoes the
    /// addition together with whatever it was for.
    /// </summary>
    public static async Task<bool> TryAddAsync(
        HelpdeskDbContext db, Guid tenantId, string metric, string period, long amount, long? limit, CancellationToken ct)
    {
        var max = limit ?? long.MaxValue;
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO usage_counters (tenant_id, metric, period, value)
            SELECT {tenantId}, {metric}, {period}, {amount}
            WHERE {amount} <= {max}
            ON CONFLICT (tenant_id, metric, period) DO UPDATE
                SET value = usage_counters.value + EXCLUDED.value
                WHERE usage_counters.value + EXCLUDED.value <= {max}
            """, ct);
        return changed == 1;
    }

    /// <summary>Gives back what a deleted attachment used.</summary>
    public static Task ReleaseAsync(HelpdeskDbContext db, Guid tenantId, string metric, string period, long amount, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE usage_counters SET value = GREATEST(value - {amount}, 0)
            WHERE tenant_id = {tenantId} AND metric = {metric} AND period = {period}
            """, ct);

    public static async Task<long> GetAsync(HelpdeskDbContext db, Guid tenantId, string metric, string period, CancellationToken ct) =>
        await db.UsageCounters.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Metric == metric && c.Period == period)
            .Select(c => (long?)c.Value)
            .SingleOrDefaultAsync(ct) ?? 0;

    public static Task<long> TicketsThisMonthAsync(HelpdeskDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct) =>
        GetAsync(db, tenantId, UsageMetric.Tickets, UsageMetric.Month(now), ct);

    public static Task<long> StorageAsync(HelpdeskDbContext db, Guid tenantId, CancellationToken ct) =>
        GetAsync(db, tenantId, UsageMetric.StorageBytes, UsageMetric.AllTime, ct);
}
