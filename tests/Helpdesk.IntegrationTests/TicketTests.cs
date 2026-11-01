using System.Net;
using System.Net.Http.Json;
using Helpdesk.IntegrationTests.Support;

namespace Helpdesk.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TicketTests(ApiFactory factory)
{
    [Fact]
    public async Task Numbers_start_at_1001_and_count_up_per_organization()
    {
        using var owner = await factory.SignUpAsync();

        var first = await owner.OpenTicketAsync("Birinci");
        var second = await owner.OpenTicketAsync("İkinci");

        Assert.Equal(1001, first.Number);
        Assert.Equal(1002, second.Number);
        Assert.Equal("Open", first.Status);
        Assert.Equal("Agent", first.Channel);
    }

    [Fact]
    public async Task Concurrent_tickets_get_distinct_numbers_without_gaps()
    {
        using var owner = await factory.SignUpAsync();

        var tickets = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => owner.OpenTicketAsync($"Talep {i}")));

        Assert.Equal(Enumerable.Range(1001, 20).Select(n => (long)n), tickets.Select(t => t.Number).Order());
    }

    [Fact]
    public async Task A_reply_waits_for_the_customer_and_a_note_changes_nothing()
    {
        using var owner = await factory.SignUpAsync();
        var ticket = await owner.OpenTicketAsync();

        var note = await owner.Http.PostAsJsonAsync($"/api/tickets/{ticket.Number}/comments", new { body = "Kargo firmasını aradım.", @internal = true });
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        Assert.Equal("Open", (await owner.Http.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Number}", TestApi.Json))!.Status);

        var reply = await owner.Http.PostAsJsonAsync($"/api/tickets/{ticket.Number}/comments", new { body = "Yarın teslim edilecek." });
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
        Assert.Equal("Pending", (await owner.Http.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Number}", TestApi.Json))!.Status);

        var comments = (await owner.Http.GetFromJsonAsync<List<CommentDto>>($"/api/tickets/{ticket.Number}/comments", TestApi.Json))!;
        Assert.Equal(new[] { false, true, false }, comments.Select(c => c.IsInternal));
        Assert.Equal(new[] { false, true, true }, comments.Select(c => c.ByTeam));
    }

    [Fact]
    public async Task Update_changes_only_what_is_sent_and_closed_stays_closed()
    {
        using var owner = await factory.SignUpAsync();
        var ticket = await owner.OpenTicketAsync();

        var assigned = await owner.Http.PatchAsJsonAsync($"/api/tickets/{ticket.Number}", new { priority = "Urgent", assigneeId = owner.Id });
        var body = await assigned.ReadAsync<TicketDto>();
        Assert.Equal("Urgent", body.Priority);
        Assert.Equal(owner.Id, body.Assignee!.Id);
        Assert.Equal(ticket.Subject, body.Subject);

        await owner.Http.PatchAsJsonAsync($"/api/tickets/{ticket.Number}", new { status = "Closed" });
        var reopen = await owner.Http.PatchAsJsonAsync($"/api/tickets/{ticket.Number}", new { status = "Open" });
        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode);

        var reply = await owner.Http.PostAsJsonAsync($"/api/tickets/{ticket.Number}/comments", new { body = "Merhaba" });
        Assert.Equal(HttpStatusCode.Conflict, reply.StatusCode);
    }

    [Fact]
    public async Task Lists_filter_by_status_assignee_and_text()
    {
        using var owner = await factory.SignUpAsync();
        var mine = await owner.OpenTicketAsync("Şifre sıfırlama", "ali@example.com");
        await owner.OpenTicketAsync("İade talebi");
        await owner.Http.PatchAsJsonAsync($"/api/tickets/{mine.Number}", new { assigneeId = owner.Id, status = "Solved" });

        Assert.Equal(new[] { mine.Number }, await NumbersAsync(owner, "?assignee=me"));
        Assert.Equal(new[] { mine.Number }, await NumbersAsync(owner, "?status=Solved"));
        Assert.Equal(new[] { mine.Number }, await NumbersAsync(owner, "?q=şifre"));
        Assert.Equal(new[] { mine.Number }, await NumbersAsync(owner, "?q=ali%40example"));
        Assert.Equal(new[] { mine.Number }, await NumbersAsync(owner, $"?q=%23{mine.Number}"));
        Assert.Single(await NumbersAsync(owner, "?assignee=unassigned"));
        Assert.Empty(await NumbersAsync(owner, "?q=100%25"));

        var paged = await owner.Http.GetFromJsonAsync<TicketPageDto>("/api/tickets?pageSize=1&page=2", TestApi.Json);
        Assert.Equal(2, paged!.Total);
        Assert.Single(paged.Items);

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Http.GetAsync("/api/tickets?pageSize=500")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Http.GetAsync("/api/tickets?assignee=someone")).StatusCode);
    }

    [Fact]
    public async Task Invalid_tickets_are_rejected()
    {
        using var owner = await factory.SignUpAsync();

        var response = await owner.Http.PostAsJsonAsync("/api/tickets", new
        {
            subject = "",
            requesterName = "Mehmet",
            requesterEmail = "not-an-email",
            message = "x",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await NumbersAsync(owner, string.Empty));
    }

    [Fact]
    public async Task Members_are_listed_owner_first()
    {
        using var owner = await factory.SignUpAsync(name: "Ayşe Yılmaz");

        var members = await owner.Http.GetFromJsonAsync<List<MemberDto>>("/api/members", TestApi.Json);

        var member = Assert.Single(members!);
        Assert.Equal("Owner", member.Role);
        Assert.Equal("Ayşe Yılmaz", member.DisplayName);
    }

    [Fact]
    public async Task Customers_open_follow_and_answer_tickets_with_their_key()
    {
        using var owner = await factory.SignUpAsync(organization: "Kargo Hızlı");
        using var anonymous = factory.CreateClient();

        var form = await anonymous.GetFromJsonAsync<PublicOrganizationDto>($"/api/public/{owner.Slug}", TestApi.Json);
        Assert.Equal("Kargo Hızlı", form!.Name);

        var created = await factory.OpenPublicTicketAsync(owner.Slug);
        Assert.Equal(1001, created.Number);

        await owner.Http.PostAsJsonAsync($"/api/tickets/{created.Number}/comments", new { body = "İnceliyoruz.", @internal = false });
        await owner.Http.PostAsJsonAsync($"/api/tickets/{created.Number}/comments", new { body = "Muhasebeye sor.", @internal = true });

        using var follow = new HttpRequestMessage(HttpMethod.Get, $"/api/public/{owner.Slug}/tickets/{created.Number}");
        follow.Headers.Add("X-Ticket-Key", created.AccessKey);
        var ticket = await (await anonymous.SendAsync(follow)).ReadAsync<PublicTicketDto>();
        Assert.Equal("Pending", ticket.Status);
        Assert.Equal(new[] { "Geçen ayın faturası iki kez kesilmiş.", "İnceliyoruz." }, ticket.Messages.Select(m => m.Body));

        using var reply = new HttpRequestMessage(HttpMethod.Post, $"/api/public/{owner.Slug}/tickets/{created.Number}/replies")
        {
            Content = JsonContent.Create(new { message = "Teşekkürler." }),
        };
        reply.Headers.Add("X-Ticket-Key", created.AccessKey);
        var answered = await (await anonymous.SendAsync(reply)).ReadAsync<PublicTicketDto>();
        Assert.Equal("Open", answered.Status);

        var list = await owner.Http.GetFromJsonAsync<TicketPageDto>("/api/tickets", TestApi.Json);
        Assert.Equal("Web", Assert.Single(list!.Items).Channel);
    }

    [Fact]
    public async Task A_wrong_key_looks_like_a_missing_ticket()
    {
        using var owner = await factory.SignUpAsync();
        var created = await factory.OpenPublicTicketAsync(owner.Slug);
        using var anonymous = factory.CreateClient();

        foreach (var (number, key) in new[] { (created.Number, "wrong"), (created.Number + 1, created.AccessKey), (created.Number, (string?)null) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/public/{owner.Slug}/tickets/{number}");
            if (key is not null)
            {
                request.Headers.Add("X-Ticket-Key", key);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.SendAsync(request)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/public/no-such-org/")).StatusCode);
    }

    [Fact]
    public async Task Ticket_endpoints_need_a_member()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/tickets")).StatusCode);
    }

    private static async Task<List<long>> NumbersAsync(Person person, string query) =>
        (await person.Http.GetFromJsonAsync<TicketPageDto>($"/api/tickets{query}", TestApi.Json))!.Items.Select(t => t.Number).ToList();
}

public sealed record PublicOrganizationDto(string Name, string Slug);
