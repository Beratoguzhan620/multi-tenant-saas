namespace Helpdesk.Core.Tenants;

/// <summary>A person's place in one organization. The same person can belong to several organizations.</summary>
public sealed class Membership
{
    private Membership()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Role Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public static Membership Create(Guid tenantId, Guid userId, Role role, DateTimeOffset now) =>
        new() { TenantId = tenantId, UserId = userId, Role = role, JoinedAt = now };

    public bool Can(Role needed) => Role >= needed;

    /// <summary>Changes a role. Ownership is handed over, never given by changing a role.</summary>
    public void ChangeRole(Role role)
    {
        if (role == Role.Owner || Role == Role.Owner)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Ownership changes hands through a transfer, not a role change.");
        }

        Role = role;
    }

    internal void BecomeOwner() => Role = Role.Owner;

    internal void StepDown() => Role = Role.Admin;

    /// <summary>Hands the organization to another member; the old owner stays as an admin.</summary>
    public static void TransferOwnership(Membership from, Membership to)
    {
        if (from.Role != Role.Owner)
        {
            throw new DomainException(ErrorCodes.Forbidden, "Only the owner hands over the organization.");
        }

        if (from.TenantId != to.TenantId || from.UserId == to.UserId)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Ownership goes to another member of the same organization.");
        }

        to.BecomeOwner();
        from.StepDown();
    }
}
