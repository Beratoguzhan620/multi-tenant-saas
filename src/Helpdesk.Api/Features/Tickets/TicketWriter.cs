using System.Security.Cryptography;
using Helpdesk.Core;
using Helpdesk.Core.Tenants;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Usage;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Usage;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Tickets;

/// <summary>Opens tickets for the team's form and for the public one, so both number and store them the same way.</summary>
internal static class TicketWriter
{
    /// <summary>
    /// Counts the ticket against the plan, takes the next number and stores the ticket with the customer's first
    /// message in one transaction: a failed insert gives the number and the count back, so numbers have no gaps.
    /// </summary>
    public static async Task<(Ticket Ticket, string AccessKey)> OpenAsync(
        HelpdeskDbContext db, Guid tenantId, string subject, string requesterName, string requesterEmail, string message,
        TicketChannel channel, TicketPriority priority, Guid? assigneeId, DateTimeOffset now, CancellationToken ct)
    {
        var accessKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var plan = Plan.For(await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Plan).SingleAsync(ct));
        if (!await UsageCounters.TryAddAsync(db, tenantId, UsageMetric.Tickets, UsageMetric.Month(now), 1, plan.TicketsPerMonth, ct))
        {
            throw new DomainException(ErrorCodes.PlanLimitReached,
                $"This organization has opened its {plan.TicketsPerMonth} tickets for the month on the {plan.Name} plan.");
        }

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
