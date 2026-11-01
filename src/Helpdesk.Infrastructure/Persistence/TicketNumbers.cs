using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Infrastructure.Persistence;

public static class TicketNumbers
{
    /// <summary>Takes the organization's next ticket number. Call it inside the transaction that stores the ticket.</summary>
    public static async Task<long> NextAsync(HelpdeskDbContext db, Guid tenantId, CancellationToken ct) =>
        (await db.Database
            .SqlQuery<long>($"UPDATE tenants SET last_ticket_number = last_ticket_number + 1 WHERE id = {tenantId} RETURNING last_ticket_number AS \"Value\"")
            .ToListAsync(ct))
        .Single();
}
