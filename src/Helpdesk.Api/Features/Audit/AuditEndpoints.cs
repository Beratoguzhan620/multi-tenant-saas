using Helpdesk.Api.Common;
using Helpdesk.Api.Features.Auth;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Audit;

public sealed record AuditEventResponse(Guid Id, string ActorName, string Action, string Target, string? Details, DateTimeOffset At);

/// <summary><c>Next</c> is the cursor for the following page; null on the last one.</summary>
public sealed record AuditPage(IReadOnlyList<AuditEventResponse> Items, Guid? Next);

internal static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit", List).WithTags("Audit log").RequireAuthorization(AuthSetup.AdminPolicy)
            .WithSummary("Who changed what (admin)")
            .WithDescription("Newest first. Pass `before` (the previous page's `next`) for older events; filter by `action`, like `member.removed`.");
        return app;
    }

    private static async Task<Results<Ok<AuditPage>, ValidationProblem>> List(
        HelpdeskDbContext db, CancellationToken ct, Guid? before = null, string? action = null, int limit = 50)
    {
        var validation = new Validation().Require(limit is >= 1 and <= 200, "limit", "A page has 1 to 200 events.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        // Newest first by time, the id breaking ties; the cursor is the last event of the previous page.
        var query = db.AuditEvents.AsNoTracking();
        if (before is { } cursor)
        {
            var at = await db.AuditEvents.Where(e => e.Id == cursor).Select(e => (DateTimeOffset?)e.At).SingleOrDefaultAsync(ct);
            if (at is { } time)
            {
                query = query.Where(e => e.At < time || (e.At == time && e.Id < cursor));
            }
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(e => e.Action == action);
        }

        var events = await query.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Take(limit + 1)
            .Select(e => new AuditEventResponse(e.Id, e.ActorName, e.Action, e.Target, e.Details, e.At))
            .ToListAsync(ct);
        var next = events.Count > limit ? events[limit - 1].Id : (Guid?)null;
        return TypedResults.Ok(new AuditPage(events.Take(limit).ToList(), next));
    }
}
