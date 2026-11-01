using System.Threading.RateLimiting;
using Helpdesk.Api.Common;
using Helpdesk.Core.Tenants;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Helpdesk.Api.Features.Plans;

/// <summary>
/// One busy organization must not slow down the others: every organization gets its own budget of requests per
/// minute, sized by its plan and shared by all its members. The plan is cached for half a minute and dropped
/// as soon as it changes.
/// </summary>
internal static class TenantRateLimit
{
    private const string PlanItem = "helpdesk.plan";

    public static string CacheKey(Guid tenantId) => $"plan:{tenantId}";

    /// <summary>Runs after authentication and before the rate limiter, so the limiter knows the caller's plan.</summary>
    public static IApplicationBuilder UseTenantPlan(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.User.TenantId() is { } tenantId)
        {
            var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
            var plan = await cache.GetOrCreateAsync(CacheKey(tenantId), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                var db = context.RequestServices.GetRequiredService<HelpdeskDbContext>();
                return await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => (PlanId?)t.Plan).SingleOrDefaultAsync(context.RequestAborted);
            });
            if (plan is not null)
            {
                context.Items[PlanItem] = (tenantId, plan.Value);
            }
        }

        await next(context);
    });

    public static PartitionedRateLimiter<HttpContext> Limiter() => PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (context.Items[PlanItem] is not (Guid tenantId, PlanId planId))
        {
            return RateLimitPartition.GetNoLimiter(string.Empty);
        }

        var plan = Plan.For(planId);
        return RateLimitPartition.GetFixedWindowLimiter($"{tenantId}:{planId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = plan.RequestsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
}
