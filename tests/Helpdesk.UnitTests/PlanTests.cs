using Helpdesk.Core;
using Helpdesk.Core.Audit;
using Helpdesk.Core.Tenants;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Usage;

namespace Helpdesk.UnitTests;

public sealed class PlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Plans_grow_from_free_to_business()
    {
        Assert.Equal(new[] { PlanId.Free, PlanId.Team, PlanId.Business }, Plan.All.Select(p => p.Id));
        Assert.True(Plan.Free.Seats < Plan.Team.Seats && Plan.Team.Seats < Plan.Business.Seats);
        Assert.Null(Plan.Business.TicketsPerMonth);
        Assert.Same(Plan.Team, Plan.For(PlanId.Team));
        Assert.Throws<DomainException>(() => Plan.For((PlanId)9));
    }

    [Fact]
    public void A_downgrade_is_blocked_by_seats_and_storage_in_use()
    {
        var blockers = Plan.Free.Blockers(seatsUsed: 5, storageUsed: 150 * Plan.MiB);

        Assert.Equal(2, blockers.Count);
        Assert.Contains("Remove 2", blockers[0], StringComparison.Ordinal);
        Assert.Empty(Plan.Free.Blockers(seatsUsed: 3, storageUsed: 100 * Plan.MiB));
    }

    [Fact]
    public void The_month_is_the_utc_calendar_month()
    {
        Assert.Equal("2026-11", UsageMetric.Month(new DateTimeOffset(2026, 12, 1, 1, 0, 0, TimeSpan.FromHours(3))));
    }

    [Fact]
    public void Changing_to_the_same_plan_is_refused()
    {
        var (tenant, _) = Tenant.Create("Kargo", "kargo", Guid.NewGuid(), Now);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => tenant.ChangePlan(PlanId.Free, Now)).Code);

        tenant.ChangePlan(PlanId.Team, Now.AddDays(1));
        Assert.Equal(PlanId.Team, tenant.Plan);
        Assert.Equal(Now.AddDays(1), tenant.PlanChangedAt);
    }
}

public sealed class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Hash = new byte[32];

    [Fact]
    public void An_invitation_lasts_a_week_and_becomes_a_membership()
    {
        var tenantId = Guid.NewGuid();
        var invitation = Invitation.Create(tenantId, " Ali@Example.com ", Role.Agent, Hash, Guid.NewGuid(), Role.Admin, Now);

        Assert.Equal("ali@example.com", invitation.Email);
        Assert.Equal(Now.AddDays(7), invitation.ExpiresAt);

        var membership = invitation.Accept(Guid.NewGuid(), "ali@example.com", Now.AddDays(6));
        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal(Role.Agent, membership.Role);
    }

    [Fact]
    public void Nobody_is_invited_as_owner_or_above_the_inviter()
    {
        Assert.Throws<DomainException>(() => Invitation.Create(Guid.NewGuid(), "a@example.com", Role.Owner, Hash, Guid.NewGuid(), Role.Owner, Now));
        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<DomainException>(() =>
            Invitation.Create(Guid.NewGuid(), "a@example.com", Role.Admin, Hash, Guid.NewGuid(), Role.Agent, Now)).Code);
    }

    [Fact]
    public void Expired_or_someone_elses_invitations_are_refused()
    {
        var invitation = Invitation.Create(Guid.NewGuid(), "ali@example.com", Role.Agent, Hash, Guid.NewGuid(), Role.Owner, Now);

        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => invitation.Accept(Guid.NewGuid(), "ali@example.com", Now.AddDays(7))).Code);
        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<DomainException>(() => invitation.Accept(Guid.NewGuid(), "veli@example.com", Now)).Code);
    }
}

public sealed class AttachmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static Ticket NewTicket() =>
        Ticket.Open(Guid.NewGuid(), 1001, "Konu", "Ad", "a@example.com", TicketChannel.Web, TicketPriority.Normal, new byte[32], Now);

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\Users\\ayse\\fatura.pdf", "fatura.pdf")]
    [InlineData(".hidden", "hidden")]
    [InlineData("a\"b\u0001.txt", "ab.txt")]
    [InlineData("", "file")]
    public void File_names_are_kept_for_display_only(string given, string expected)
    {
        Assert.Equal(expected, Attachment.CleanName(given));
    }

    [Fact]
    public void The_storage_key_comes_from_ids_not_the_name()
    {
        var ticket = NewTicket();
        var attachment = Attachment.Create(ticket, "../x.pdf", "application/pdf; charset=binary", 1234, Guid.NewGuid(), Now);

        Assert.Equal($"{ticket.TenantId:N}/{attachment.Id:N}", attachment.StorageKey);
        Assert.Equal("application/pdf", attachment.ContentType);
    }

    [Theory]
    [InlineData("text/html", 10)]
    [InlineData("image/svg+xml", 10)]
    [InlineData("application/pdf", 0)]
    [InlineData("application/pdf", Attachment.MaxBytes + 1)]
    public void Unsafe_types_and_sizes_are_refused(string type, long size)
    {
        Assert.Throws<DomainException>(() => Attachment.Create(NewTicket(), "f", type, size, null, Now));
    }

    [Fact]
    public void Audit_events_trim_long_text()
    {
        var e = AuditEvent.Record(Guid.NewGuid(), null, new string('a', 150), AuditActions.PlanChanged, new string('b', 300), new string('c', 900), Now);

        Assert.Equal(100, e.ActorName.Length);
        Assert.Equal(200, e.Target.Length);
        Assert.Equal(500, e.Details!.Length);
    }
}
