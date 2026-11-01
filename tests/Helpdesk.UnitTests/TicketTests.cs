using Helpdesk.Core;
using Helpdesk.Core.Tickets;

namespace Helpdesk.UnitTests;

public sealed class TicketTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Hash = new byte[32];

    private static Ticket NewTicket() =>
        Ticket.Open(Guid.NewGuid(), 1001, "  Kargom gelmedi ", "Mehmet Kaya", " Mehmet@Example.com ", TicketChannel.Web, TicketPriority.Normal, Hash, Now);

    [Fact]
    public void Opens_trimmed_and_open()
    {
        var ticket = NewTicket();

        Assert.Equal("Kargom gelmedi", ticket.Subject);
        Assert.Equal("mehmet@example.com", ticket.RequesterEmail);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(Now, ticket.UpdatedAt);
    }

    [Theory]
    [InlineData("", "Ad", "a@example.com")]
    [InlineData("Konu", "", "a@example.com")]
    [InlineData("Konu", "Ad", "not-an-email")]
    [InlineData("Konu", "Ad", "a@localhost")]
    [InlineData("Konu", "Ad", "Ad <a@example.com>")]
    public void Rejects_missing_or_invalid_fields(string subject, string name, string email)
    {
        var error = Assert.Throws<DomainException>(() =>
            Ticket.Open(Guid.NewGuid(), 1001, subject, name, email, TicketChannel.Web, TicketPriority.Normal, Hash, Now));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }

    [Fact]
    public void Rejects_a_subject_that_is_too_long()
    {
        Assert.Throws<DomainException>(() => NewTicket().Rename(new string('a', Ticket.SubjectMaxLength + 1), Now));
    }

    [Fact]
    public void Requires_a_sha256_key_hash()
    {
        Assert.Throws<ArgumentException>(() =>
            Ticket.Open(Guid.NewGuid(), 1001, "Konu", "Ad", "a@example.com", TicketChannel.Web, TicketPriority.Normal, new byte[4], Now));
    }

    [Fact]
    public void A_team_reply_waits_for_the_customer_and_theirs_reopens_it()
    {
        var ticket = NewTicket();

        Comment.Write(ticket, Guid.NewGuid(), "Ayşe", "Bakıyoruz", isInternal: false, Now.AddMinutes(5));
        Assert.Equal(TicketStatus.Pending, ticket.Status);

        Comment.Write(ticket, authorId: null, "Mehmet Kaya", "Teşekkürler", isInternal: false, Now.AddMinutes(9));
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(Now.AddMinutes(9), ticket.UpdatedAt);
    }

    [Fact]
    public void An_internal_note_keeps_the_status()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(TicketStatus.Solved, Now);

        var note = Comment.Write(ticket, Guid.NewGuid(), "Ayşe", "Not", isInternal: true, Now);

        Assert.True(note.IsInternal);
        Assert.Equal(TicketStatus.Solved, ticket.Status);
    }

    [Fact]
    public void Customers_cannot_write_internal_notes()
    {
        var error = Assert.Throws<DomainException>(() => Comment.Write(NewTicket(), authorId: null, "Mehmet", "Gizli", isInternal: true, Now));
        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_messages_are_rejected(string body)
    {
        Assert.Throws<DomainException>(() => Comment.Write(NewTicket(), Guid.NewGuid(), "Ayşe", body, isInternal: false, Now));
    }

    [Fact]
    public void A_closed_ticket_stays_closed_and_takes_no_replies()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(TicketStatus.Closed, Now);

        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => ticket.ChangeStatus(TicketStatus.Open, Now)).Code);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => Comment.Write(ticket, Guid.NewGuid(), "Ayşe", "Merhaba", false, Now)).Code);
        ticket.ChangeStatus(TicketStatus.Closed, Now);
    }

    [Fact]
    public void Unknown_status_and_priority_are_rejected()
    {
        var ticket = NewTicket();
        Assert.Throws<DomainException>(() => ticket.ChangeStatus((TicketStatus)42, Now));
        Assert.Throws<DomainException>(() => ticket.ChangePriority((TicketPriority)42, Now));
    }
}
