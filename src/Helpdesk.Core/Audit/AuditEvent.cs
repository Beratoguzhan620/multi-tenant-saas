namespace Helpdesk.Core.Audit;

/// <summary>
/// Who changed what in an organization: members, roles, the plan, settings, and deletions. Written in the same
/// transaction as the change, and only ever added to.
/// </summary>
public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid? ActorId { get; private set; }

    public string ActorName { get; private set; } = string.Empty;

    /// <summary>A stable name such as <c>member.role_changed</c>; see <see cref="AuditActions"/>.</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>What it happened to, in words: an email, a ticket number, a plan.</summary>
    public string Target { get; private set; } = string.Empty;

    public string? Details { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static AuditEvent Record(Guid tenantId, Guid? actorId, string actorName, string action, string target, string? details, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            ActorId = actorId,
            ActorName = actorName.Length <= 100 ? actorName : actorName[..100],
            Action = action,
            Target = target.Length <= 200 ? target : target[..200],
            Details = details is { Length: > 500 } ? details[..500] : details,
            At = now,
        };
}

public static class AuditActions
{
    public const string OrganizationRenamed = "organization.renamed";
    public const string PlanChanged = "plan.changed";
    public const string MemberInvited = "member.invited";
    public const string InvitationRevoked = "invitation.revoked";
    public const string MemberJoined = "member.joined";
    public const string MemberRoleChanged = "member.role_changed";
    public const string MemberRemoved = "member.removed";
    public const string OwnershipTransferred = "ownership.transferred";
    public const string TicketAssigned = "ticket.assigned";
    public const string TicketStatusChanged = "ticket.status_changed";
    public const string AttachmentDeleted = "attachment.deleted";
}
