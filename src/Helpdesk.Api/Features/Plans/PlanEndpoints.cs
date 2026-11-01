using Helpdesk.Api.Features.Audit;
using Helpdesk.Api.Features.Auth;
using Helpdesk.Core;
using Helpdesk.Core.Audit;
using Helpdesk.Core.Tenants;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Tenancy;
using Helpdesk.Infrastructure.Usage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Helpdesk.Api.Features.Plans;

public sealed record PlanResponse(PlanId Id, string Name, int Seats, int? TicketsPerMonth, long StorageBytes, int RequestsPerMinute, int PriceUsd)
{
    public static PlanResponse From(Plan plan) =>
        new(plan.Id, plan.Name, plan.Seats, plan.TicketsPerMonth, plan.StorageBytes, plan.RequestsPerMinute, plan.PriceUsd);
}

public sealed record UsageResponse(PlanResponse Plan, int Seats, long TicketsThisMonth, long StorageBytes);

public sealed record ChangePlanRequest(PlanId Plan);

internal static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/plans", () => TypedResults.Ok(Plan.All.Select(PlanResponse.From).ToList()))
            .WithTags("Plans").AllowAnonymous().WithSummary("What each plan allows");
        app.MapGet("/api/organization/usage", Usage).WithTags("Plans").RequireAuthorization(AuthSetup.MemberPolicy)
            .WithSummary("Your plan and how much of it is used");
        app.MapPut("/api/organization/plan", Change).WithTags("Plans").RequireAuthorization(AuthSetup.OwnerPolicy)
            .WithSummary("Change plan (owner)")
            .WithDescription("Upgrades apply at once. A downgrade is refused with the reasons while the organization uses more seats or storage than the smaller plan has. There is no billing in this project.");
        return app;
    }

    private static async Task<Ok<UsageResponse>> Usage(HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tenantId = tenant.RequiredTenantId;
        var plan = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Plan).SingleAsync(ct);
        return TypedResults.Ok(new UsageResponse(
            PlanResponse.From(Plan.For(plan)),
            await Seats.UsedAsync(db, tenantId, now, ct),
            await UsageCounters.TicketsThisMonthAsync(db, tenantId, now, ct),
            await UsageCounters.StorageAsync(db, tenantId, ct)));
    }

    private static async Task<Ok<UsageResponse>> Change(
        ChangePlanRequest request, HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, IMemoryCache cache, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tenantId = tenant.RequiredTenantId;
        var target = Plan.For(request.Plan);

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // The same lock invitations take, so no seat is handed out while the plan shrinks.
            var organization = (await db.Tenants.FromSql($"SELECT * FROM tenants WHERE id = {tenantId} FOR UPDATE").ToListAsync(ct)).Single();
            var from = Plan.For(organization.Plan);
            var blockers = target.Blockers(await Seats.UsedAsync(db, tenantId, now, ct), await UsageCounters.StorageAsync(db, tenantId, ct));
            if (blockers.Count > 0)
            {
                throw new DomainException(ErrorCodes.InvalidState, string.Join(" ", blockers));
            }

            organization.ChangePlan(target.Id, now);
            await AuditLog.AddAsync(db, tenant, AuditActions.PlanChanged, target.Name, $"{from.Name} → {target.Name}", now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        cache.Remove(TenantRateLimit.CacheKey(tenantId));
        return await Usage(db, tenant, clock, ct);
    }
}
