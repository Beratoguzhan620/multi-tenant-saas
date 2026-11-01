using System.Security.Cryptography;
using Helpdesk.Core.Tickets;
using Helpdesk.Infrastructure.Persistence;

namespace Helpdesk.Api.Features.Tickets;

/// <summary>Opens tickets for the team's form and for the public one, so both number and store them the same way.</summary>
internal static class TicketWriter
{
    /// <summary>
    /// Takes the next number and stores the ticket with the customer's first message in one transaction: a failed
    /// insert gives the number back, so numbers have no gaps.
    /// </summary>
    public static async Task<(Ticket Ticket, string AccessKey)> OpenAsync(
        HelpdeskDbContext db, Guid tenantId, string subject, string requesterName, string requesterEmail, string message,
        TicketChannel channel, TicketPriority priority, Guid? assigneeId, DateTimeOffset now, CancellationToken ct)
    {
        var accessKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var number = await TicketNumbers.NextAsync(db, tenantId, ct);
        var ticket = Ticket.Open(tenantId, number, subject, requesterName, requesterEmail, channel, priority, HashKey(accessKey), now);
        if (assigneeId is not null)
        {
            ticket.Assign(assigneeId, now);
        }

        var first = Comment.Write(ticket, authorId: null, ticket.RequesterName, message, isInternal: false, now);
        db.Tickets.Add(ticket);
        db.Comments.Add(first);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (ticket, accessKey);
    }

    public static byte[] HashKey(string accessKey) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(accessKey));

    public static bool KeyMatches(Ticket ticket, string? accessKey) =>
        !string.IsNullOrEmpty(accessKey)
        && accessKey.Length <= 100
        && CryptographicOperations.FixedTimeEquals(ticket.AccessKeyHash, HashKey(accessKey));
}
