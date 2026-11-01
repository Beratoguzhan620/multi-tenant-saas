using Helpdesk.Core;
using Helpdesk.Core.Tenants;

namespace Helpdesk.UnitTests;

public sealed class MembershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.CreateVersion7();

    [Theory]
    [InlineData(Role.Agent, Role.Agent, true)]
    [InlineData(Role.Agent, Role.Admin, false)]
    [InlineData(Role.Admin, Role.Agent, true)]
    [InlineData(Role.Admin, Role.Owner, false)]
    [InlineData(Role.Owner, Role.Admin, true)]
    public void Each_role_can_do_what_the_ones_below_it_can(Role role, Role needed, bool allowed) =>
        Assert.Equal(allowed, Membership.Create(Tenant, Guid.NewGuid(), role, Now).Can(needed));

    [Fact]
    public void Ownership_is_handed_over_and_the_old_owner_stays_as_admin()
    {
        var owner = Membership.Create(Tenant, Guid.NewGuid(), Role.Owner, Now);
        var agent = Membership.Create(Tenant, Guid.NewGuid(), Role.Agent, Now);

        Membership.TransferOwnership(owner, agent);

        Assert.Equal(Role.Owner, agent.Role);
        Assert.Equal(Role.Admin, owner.Role);
    }

    [Fact]
    public void Only_the_owner_hands_over_and_only_within_the_organization()
    {
        var admin = Membership.Create(Tenant, Guid.NewGuid(), Role.Admin, Now);
        var agent = Membership.Create(Tenant, Guid.NewGuid(), Role.Agent, Now);
        var owner = Membership.Create(Tenant, Guid.NewGuid(), Role.Owner, Now);
        var elsewhere = Membership.Create(Guid.CreateVersion7(), Guid.NewGuid(), Role.Agent, Now);

        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<DomainException>(() => Membership.TransferOwnership(admin, agent)).Code);
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => Membership.TransferOwnership(owner, elsewhere)).Code);
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => Membership.TransferOwnership(owner, owner)).Code);
    }

    [Fact]
    public void Roles_change_but_never_into_or_out_of_ownership()
    {
        var agent = Membership.Create(Tenant, Guid.NewGuid(), Role.Agent, Now);
        var owner = Membership.Create(Tenant, Guid.NewGuid(), Role.Owner, Now);

        agent.ChangeRole(Role.Admin);

        Assert.Equal(Role.Admin, agent.Role);
        Assert.Throws<DomainException>(() => agent.ChangeRole(Role.Owner));
        Assert.Throws<DomainException>(() => owner.ChangeRole(Role.Agent));
    }
}
