using Helpdesk.Core.Tickets;

namespace Helpdesk.Core.Tenants;

/// <summary>
/// An open seat waiting for someone. The admin shares the link; whoever opens it with the matching email address
/// joins with the given role. Only a hash of the link's secret is stored. Open invitations count as seats, so an
/// organization cannot invite past its plan.
/// </summary>
public sealed class Invitation
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private Invitation()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public Role Role { get; private set; }

    public byte[] SecretHash { get; private set; } = [];

    public Guid InvitedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public static Invitation Create(Guid tenantId, string email, Role role, byte[] secretHash, Guid invitedBy, Role inviterRole, DateTimeOffset now)
    {
        if (!Enum.IsDefined(role) || role == Role.Owner)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Invite people as an agent or an admin.");
        }

        if (role > inviterRole)
        {
            throw new DomainException(ErrorCodes.Forbidden, "You cannot invite someone with more rights than your own.");
        }

        return new Invitation
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            Email = Ticket.CheckEmail(email),
            Role = role,
            SecretHash = secretHash is { Length: 32 } ? secretHash : throw new ArgumentException("A SHA-256 hash is 32 bytes.", nameof(secretHash)),
            InvitedBy = invitedBy,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Turns the invitation into a membership for the person who opened it.</summary>
    public Membership Accept(Guid userId, string userEmail, DateTimeOffset now)
    {
        if (IsExpired(now))
        {
            throw new DomainException(ErrorCodes.InvalidState, "This invitation has expired; ask for a new one.");
        }

        if (!string.Equals(userEmail, Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(ErrorCodes.Forbidden, "This invitation is for another email address.");
        }

        return Membership.Create(TenantId, userId, Role, now);
    }
}
