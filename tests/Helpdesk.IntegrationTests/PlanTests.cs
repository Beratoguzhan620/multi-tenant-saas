using System.Net;
using System.Net.Http.Json;
using Helpdesk.IntegrationTests.Support;

namespace Helpdesk.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PlanTests(ApiFactory factory)
{
    [Fact]
    public async Task The_plans_are_public()
    {
        using var anonymous = factory.CreateClient();
        var plans = await anonymous.GetFromJsonAsync<List<PlanDto>>("/api/plans", TestApi.Json);
        Assert.Equal(new[] { "Free", "Team", "Business" }, plans!.Select(p => p.Id));
    }

    [Fact]
    public async Task Usage_counts_seats_tickets_and_storage()
    {
        using var owner = await factory.SignUpAsync();
        await owner.InviteAsync($"{TestApi.Unique("x")}@example.com");
        await owner.OpenTicketAsync();
        await factory.OpenPublicTicketAsync(owner.Slug);

        var usage = await owner.UsageAsync();

        Assert.Equal("Free", usage.Plan.Id);
        Assert.Equal(2, usage.Seats);
        Assert.Equal(2, usage.TicketsThisMonth);
        Assert.Equal(0, usage.StorageBytes);
    }

    [Fact]
    public async Task The_free_plan_has_three_seats_counting_open_invitations()
    {
        using var owner = await factory.SignUpAsync();
        await owner.InviteAsync($"{TestApi.Unique("a")}@example.com");
        var second = await owner.InviteAsync($"{TestApi.Unique("b")}@example.com");

        var third = await owner.Http.PostAsJsonAsync("/api/invitations", new { email = $"{TestApi.Unique("c")}@example.com", role = "Agent" });
        Assert.Equal(HttpStatusCode.PaymentRequired, third.StatusCode);
        Assert.Equal("plan_limit_reached", await third.ProblemCodeAsync());

        // Inviting the same address again replaces the link without taking another seat.
        Assert.Equal(HttpStatusCode.Created, (await owner.Http.PostAsJsonAsync("/api/invitations", new { email = second.Invitation.Email, role = "Agent" })).StatusCode);

        await owner.ChangePlanAsync("Team");
        Assert.Equal(HttpStatusCode.Created, (await owner.Http.PostAsJsonAsync("/api/invitations", new { email = $"{TestApi.Unique("d")}@example.com", role = "Agent" })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_invitations_cannot_overbook_seats()
    {
        using var owner = await factory.SignUpAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            owner.Http.PostAsJsonAsync("/api/invitations", new { email = $"{TestApi.Unique($"p{i}")}@example.com", role = "Agent" })));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, (await owner.UsageAsync()).Seats);
    }

    [Fact]
    public async Task The_monthly_ticket_limit_holds_under_concurrency()
    {
        using var owner = await factory.SignUpAsync();
        await factory.ExecuteAsOwnerAsync($"""
            INSERT INTO usage_counters (tenant_id, metric, period, value)
            VALUES ('{owner.OrganizationId}', 'tickets', to_char(now() AT TIME ZONE 'utc', 'YYYY-MM'), 95)
            """);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => owner.Http.PostAsJsonAsync("/api/tickets", new
        {
            subject = $"Talep {i}",
            requesterName = "Mehmet",
            requesterEmail = "m@example.com",
            message = "Merhaba",
        })));

        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.PaymentRequired, r.StatusCode));
        Assert.Equal(100, (await owner.UsageAsync()).TicketsThisMonth);

        // Refused tickets took no number: the five that made it are #1001 to #1005.
        var page = await owner.Http.GetFromJsonAsync<TicketPageDto>("/api/tickets", TestApi.Json);
        Assert.Equal(Enumerable.Range(1001, 5).Select(n => (long)n), page!.Items.Select(t => t.Number).Order());

        using var anonymous = factory.CreateClient();
        var form = await anonymous.PostAsJsonAsync($"/api/public/{owner.Slug}/tickets", new { name = "Z", email = "z@example.com", subject = "s", message = "m" });
        Assert.Equal(HttpStatusCode.PaymentRequired, form.StatusCode);
    }

    [Fact]
    public async Task A_downgrade_is_refused_while_more_seats_are_in_use()
    {
        using var owner = await factory.SignUpAsync();
        await owner.ChangePlanAsync("Team");
        var invitations = new List<InvitationCreatedDto>();
        for (var i = 0; i < 4; i++)
        {
            invitations.Add(await owner.InviteAsync($"{TestApi.Unique($"s{i}")}@example.com"));
        }

        var refused = await owner.Http.PutAsJsonAsync("/api/organization/plan", new { plan = "Free" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Remove 2", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("Team", (await owner.UsageAsync()).Plan.Id);

        foreach (var invitation in invitations.Take(2))
        {
            await owner.Http.DeleteAsync($"/api/invitations/{invitation.Invitation.Id}");
        }

        await owner.ChangePlanAsync("Free");
        Assert.Equal("Free", (await owner.UsageAsync()).Plan.Id);
    }

    [Fact]
    public async Task Only_the_owner_changes_the_plan()
    {
        using var owner = await factory.SignUpAsync();
        using var admin = await factory.AddMemberAsync(owner, "Admin");

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Http.PutAsJsonAsync("/api/organization/plan", new { plan = "Team" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Http.PutAsJsonAsync("/api/organization/plan", new { plan = "Free" })).StatusCode);
    }

    [Fact]
    public async Task Each_organization_has_its_own_request_budget()
    {
        using var busy = await factory.SignUpAsync();
        using var quiet = await factory.SignUpAsync();

        // Free allows 120 a minute. A window can roll over during the loop, so allow for a second budget.
        HttpResponseMessage? limited = null;
        var ok = 0;
        for (var i = 0; i < 300 && limited is null; i++)
        {
            var response = await busy.Http.GetAsync("/api/organization/usage");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
            }
            else
            {
                ok++;
            }
        }

        Assert.NotNull(limited);
        Assert.True(ok >= 120, $"Only {ok} requests went through before the limit.");
        Assert.Equal("rate_limited", await limited.ProblemCodeAsync());
        Assert.NotNull(limited.Headers.RetryAfter);

        Assert.Equal(HttpStatusCode.OK, (await quiet.Http.GetAsync("/api/organization/usage")).StatusCode);
    }
}
