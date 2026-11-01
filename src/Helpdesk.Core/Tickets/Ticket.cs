using System.Net.Mail;

namespace Helpdesk.Core.Tickets;

/// <summary>
/// A customer's request. <see cref="Number"/> is per organization (#1001, #1002, …): two organizations both have a
/// #1001, and neither can tell the other exists.
/// </summary>
public sealed class Ticket
{
    public const int SubjectMaxLength = 200;
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 254;

    private Ticket()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public long Number { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public TicketStatus Status { get; private set; }

    public TicketPriority Priority { get; private set; }

    public TicketChannel Channel { get; private set; }

    public string RequesterName { get; private set; } = string.Empty;

    public string RequesterEmail { get; private set; } = string.Empty;

    public Guid? AssigneeId { get; private set; }

    /// <summary>
    /// SHA-256 of the key the customer received when they opened the ticket; with it they can follow and answer
    /// the ticket without an account. Only the hash is stored.
    /// </summary>
    public byte[] AccessKeyHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Ticket Open(
        Guid tenantId, long number, string subject, string requesterName, string requesterEmail,
        TicketChannel channel, TicketPriority priority, byte[] accessKeyHash, DateTimeOffset now) =>
        new()
        {
            AccessKeyHash = accessKeyHash is { Length: 32 } ? accessKeyHash : throw new ArgumentException("A SHA-256 hash is 32 bytes.", nameof(accessKeyHash)),
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            Number = number,
            Subject = CheckSubject(subject),
            RequesterName = CheckName(requesterName),
            RequesterEmail = CheckEmail(requesterEmail),
            Channel = channel,
            Priority = CheckPriority(priority),
            Status = TicketStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void ChangeStatus(TicketStatus status, DateTimeOffset now)
    {
        if (!Enum.IsDefined(status))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "There is no such status.");
        }

        if (Status == TicketStatus.Closed && status != TicketStatus.Closed)
        {
            throw new DomainException(ErrorCodes.InvalidState, "A closed ticket stays closed; open a new one instead.");
        }

        Status = status;
        UpdatedAt = now;
    }

    public void ChangePriority(TicketPriority priority, DateTimeOffset now)
    {
        Priority = CheckPriority(priority);
        UpdatedAt = now;
    }

    public void Rename(string subject, DateTimeOffset now)
    {
        Subject = CheckSubject(subject);
        UpdatedAt = now;
    }

    /// <summary>Assigns the ticket; the caller has already checked that the assignee belongs to the organization.</summary>
    public void Assign(Guid? userId, DateTimeOffset now)
    {
        AssigneeId = userId;
        UpdatedAt = now;
    }

    /// <summary>A public reply from the team waits for the customer; one from the customer waits for the team again.</summary>
    public void Replied(bool byTeam, bool isInternal, DateTimeOffset now)
    {
        if (Status == TicketStatus.Closed)
        {
            throw new DomainException(ErrorCodes.InvalidState, "A closed ticket takes no more replies.");
        }

        if (!isInternal)
        {
            Status = byTeam ? TicketStatus.Pending : TicketStatus.Open;
        }

        UpdatedAt = now;
    }

    public static string CheckEmail(string? email)
    {
        var trimmed = (email ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > EmailMaxLength
            || !MailAddress.TryCreate(trimmed, out var address)
            || !string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase)
            || !address.Host.Contains('.', StringComparison.Ordinal))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A valid email address is required.");
        }

        return trimmed.ToLowerInvariant();
    }

    private static string CheckSubject(string? subject)
    {
        var trimmed = (subject ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > SubjectMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A subject has 1 to {SubjectMaxLength} characters.");
        }

        return trimmed;
    }

    private static string CheckName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A name has 1 to {NameMaxLength} characters.");
        }

        return trimmed;
    }

    private static TicketPriority CheckPriority(TicketPriority priority) =>
        Enum.IsDefined(priority) ? priority : throw new DomainException(ErrorCodes.ValidationFailed, "There is no such priority.");
}
