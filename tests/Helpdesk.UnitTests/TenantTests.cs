using Helpdesk.Core;
using Helpdesk.Core.Tenants;

namespace Helpdesk.UnitTests;

public sealed class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_organization_is_on_the_free_plan_with_its_creator_as_owner()
    {
        var owner = Guid.CreateVersion7();

        var (tenant, membership) = Tenant.Create("  Kargo Hızlı  ", "Kargo-Hizli", owner, Now);

        Assert.Equal("Kargo Hızlı", tenant.Name);
        Assert.Equal("kargo-hizli", tenant.Slug);
        Assert.Equal(PlanId.Free, tenant.Plan);
        Assert.Equal(Role.Owner, membership.Role);
        Assert.Equal(tenant.Id, membership.TenantId);
        Assert.Equal(owner, membership.UserId);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("-kargo")]
    [InlineData("kargo-")]
    [InlineData("kargo hizli")]
    [InlineData("kargo_hizli")]
    [InlineData("kargo-hızlı")]
    public void Addresses_are_lowercase_letters_digits_and_dashes(string slug) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => Tenant.Create("Kargo", slug, Guid.NewGuid(), Now)).Code);

    [Theory]
    [InlineData("")]
    [InlineData(" K ")]
    public void An_organization_needs_a_name(string name) =>
        Assert.Throws<DomainException>(() => Tenant.Create(name, "kargo", Guid.NewGuid(), Now));

    [Fact]
    public void Changing_to_the_same_plan_is_refused()
    {
        var (tenant, _) = Tenant.Create("Kargo", "kargo", Guid.NewGuid(), Now);

        tenant.ChangePlan(PlanId.Team, Now.AddDays(1));

        Assert.Equal(PlanId.Team, tenant.Plan);
        Assert.Equal(Now.AddDays(1), tenant.PlanChangedAt);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => tenant.ChangePlan(PlanId.Team, Now)).Code);
    }
}
