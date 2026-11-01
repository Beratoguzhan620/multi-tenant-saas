using Helpdesk.Core.Audit;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Audit;

internal static class AuditLog
{
    /// <summary>Adds an event to the unit of work, so it is saved with the change it describes, or not at all.</summary>
    public static async Task AddAsync(
        HelpdeskDbContext db, TenantContext tenant, string action, string target, string? details, DateTimeOffset now, CancellationToken ct)
    {
        var name = tenant.UserId is { } userId
            ? await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).SingleAsync(ct)
            : "Customer";
        db.AuditEvents.Add(AuditEvent.Record(tenant.RequiredTenantId, tenant.UserId, name, action, target, details, now));
    }
}
