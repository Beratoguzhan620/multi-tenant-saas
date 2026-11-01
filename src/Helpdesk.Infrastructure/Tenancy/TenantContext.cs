using Helpdesk.Core.Tenants;

namespace Helpdesk.Infrastructure.Tenancy;

/// <summary>
/// The organization and role of the current request, filled in once the membership has been checked against
/// the database. Everything that touches tenant data reads the tenant from here and nowhere else.
/// </summary>
public sealed class TenantContext
{
    public Guid? TenantId { get; private set; }

    public Guid? UserId { get; private set; }

    public Role? Role { get; private set; }

    public bool IsSet => TenantId is not null;

    public Guid RequiredTenantId => TenantId ?? throw new InvalidOperationException("No organization has been resolved for this request.");

    public void Set(Guid tenantId, Guid? userId, Role? role)
    {
        if (TenantId is { } current && current != tenantId)
        {
            throw new InvalidOperationException("The organization of a request cannot change halfway through.");
        }

        TenantId = tenantId;
        UserId = userId;
        Role = role;
    }
}
