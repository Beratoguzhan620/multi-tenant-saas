namespace Helpdesk.Core.Tickets;

/// <summary>A message on a ticket: a reply the customer sees, or an internal note only the team sees.</summary>
public sealed class Comment
{
    public const int BodyMaxLength = 10_000;

    private Comment()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid TicketId { get; private set; }

    /// <summary>The team member who wrote it; null when the customer wrote it.</summary>
    public Guid? AuthorId { get; private set; }

    public string AuthorName { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public bool IsInternal { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Comment Write(Ticket ticket, Guid? authorId, string authorName, string body, bool isInternal, DateTimeOffset now)
    {
        if (authorId is null && isInternal)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Only the team writes internal notes.");
        }

        var text = (body ?? string.Empty).Trim();
        if (text.Length is 0 or > BodyMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A message has 1 to {BodyMaxLength} characters.");
        }

        ticket.Replied(byTeam: authorId is not null, isInternal, now);
        return new Comment
        {
            Id = Guid.CreateVersion7(now),
            TenantId = ticket.TenantId,
            TicketId = ticket.Id,
            AuthorId = authorId,
            AuthorName = authorName,
            Body = text,
            IsInternal = isInternal,
            CreatedAt = now,
        };
    }
}
