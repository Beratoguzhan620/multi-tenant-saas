using System.Net;
using System.Net.Http.Json;
using Helpdesk.IntegrationTests.Support;

namespace Helpdesk.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TeamTests(ApiFactory factory)
{
    [Fact]
    public async Task An_invited_person_joins_with_a_new_account()
    {
        using var owner = await factory.SignUpAsync(organization: "Kargo Hızlı");
        var email = $"{TestApi.Unique("new")}@example.com";
        var invitation = await owner.InviteAsync(email);
        using var anonymous = factory.CreateClient();

        var preview = await (await anonymous.PostAsJsonAsync("/api/invitations/preview", new { token = invitation.Token })).ReadAsync<InvitationPreviewDto>();
        Assert.Equal("Kargo Hızlı", preview.OrganizationName);
        Assert.False(preview.HasAccount);

        var join = await anonymous.PostAsJsonAsync("/api/auth/join", new { token = invitation.Token, displayName = "Can Öztürk", password = TestApi.Password });
        Assert.Equal(HttpStatusCode.Created, join.StatusCode);
        var session = await join.ReadAsync<SessionDto>();
        Assert.Equal(owner.OrganizationId, session.Organization!.Id);
        Assert.Equal("Agent", session.Organization.Role);

        var members = await owner.Http.GetFromJsonAsync<List<MemberDto>>("/api/members", TestApi.Json);
        Assert.Equal(new[] { "Owner", "Agent" }, members!.Select(m => m.Role));
        Assert.Empty((await owner.Http.GetFromJsonAsync<List<InvitationDto>>("/api/invitations", TestApi.Json))!);

        var again = await anonymous.PostAsJsonAsync("/api/auth/join", new { token = invitation.Token, displayName = "Can", password = TestApi.Password });
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Someone_with_an_account_accepts_while_signed_in()
    {
        using var kargo = await factory.SignUpAsync();
        using var market = await factory.SignUpAsync();
        var invitation = await kargo.InviteAsync(market.Email, "Admin");

        var accepted = await market.Http.PostAsJsonAsync("/api/invitations/accept", new { token = invitation.Token });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var session = await accepted.ReadAsync<SessionDto>();
        Assert.Equal(kargo.OrganizationId, session.Organization!.Id);
        Assert.Equal(2, session.Organizations.Count);
    }

    [Fact]
    public async Task An_invitation_only_works_for_its_address_and_until_revoked()
    {
        using var owner = await factory.SignUpAsync();
        using var stranger = await factory.SignUpAsync();
        var invitation = await owner.InviteAsync($"{TestApi.Unique("x")}@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.Http.PostAsJsonAsync("/api/invitations/accept", new { token = invitation.Token })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.DeleteAsync($"/api/invitations/{invitation.Invitation.Id}")).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/api/invitations/preview", new { token = invitation.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/api/invitations/preview", new { token = "garbage" })).StatusCode);
    }

    [Fact]
    public async Task A_token_from_one_organization_cannot_be_replayed_against_another()
    {
        using var kargo = await factory.SignUpAsync();
        using var market = await factory.SignUpAsync();
        var invitation = await kargo.InviteAsync($"{TestApi.Unique("x")}@example.com");

        // Same secret, other organization's id in front: the invitation is looked up under that organization and is not there.
        var bytes = Convert.FromBase64String(invitation.Token.Replace('-', '+').Replace('_', '/'));
        market.OrganizationId.ToByteArray().CopyTo(bytes, 0);
        var forged = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/api/invitations/preview", new { token = forged })).StatusCode);
    }

    [Fact]
    public async Task Agents_cannot_invite_or_manage_and_admins_cannot_touch_admins()
    {
        using var owner = await factory.SignUpAsync();
        using var admin = await factory.AddMemberAsync(owner, "Admin");
        using var agent = await factory.AddMemberAsync(owner, "Agent");

        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Http.PostAsJsonAsync("/api/invitations", new { email = "a@example.com", role = "Agent" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Http.DeleteAsync($"/api/members/{admin.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Http.PatchAsJsonAsync($"/api/members/{agent.Id}", new { role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Http.DeleteAsync($"/api/members/{owner.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Http.DeleteAsync($"/api/members/{owner.Id}")).StatusCode);

        var promoted = await owner.Http.PatchAsJsonAsync($"/api/members/{agent.Id}", new { role = "Admin" });
        Assert.Equal("Admin", (await promoted.ReadAsync<MemberDto>()).Role);
    }

    [Fact]
    public async Task A_removed_member_loses_access_at_once_and_their_tickets_are_unassigned()
    {
        using var owner = await factory.SignUpAsync();
        using var agent = await factory.AddMemberAsync(owner);
        var ticket = await owner.OpenTicketAsync();
        await owner.Http.PatchAsJsonAsync($"/api/tickets/{ticket.Number}", new { assigneeId = agent.Id });

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.DeleteAsync($"/api/members/{agent.Id}")).StatusCode);

        // The agent's token is still valid; membership is checked on every request.
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Http.GetAsync("/api/tickets")).StatusCode);
        Assert.Null((await owner.Http.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Number}", TestApi.Json))!.Assignee);
    }

    [Fact]
    public async Task Agents_can_leave()
    {
        using var owner = await factory.SignUpAsync();
        using var agent = await factory.AddMemberAsync(owner);

        Assert.Equal(HttpStatusCode.NoContent, (await agent.Http.DeleteAsync($"/api/members/{agent.Id}")).StatusCode);
        Assert.Single((await owner.Http.GetFromJsonAsync<List<MemberDto>>("/api/members", TestApi.Json))!);
    }

    [Fact]
    public async Task The_owner_hands_over_and_stays_as_admin()
    {
        using var owner = await factory.SignUpAsync();
        using var admin = await factory.AddMemberAsync(owner, "Admin", name: "Deniz Arslan");

        var response = await owner.Http.PostAsJsonAsync("/api/organization/transfer", new { userId = admin.Id });

        var members = await response.ReadAsync<List<MemberDto>>();
        Assert.Equal("Owner", members.Single(m => m.Id == admin.Id).Role);
        Assert.Equal("Admin", members.Single(m => m.Id == owner.Id).Role);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Http.PutAsJsonAsync("/api/organization/plan", new { plan = "Team" })).StatusCode);
    }

    [Fact]
    public async Task The_audit_log_records_who_did_what_and_only_admins_read_it()
    {
        using var owner = await factory.SignUpAsync(name: "Ayşe Yılmaz");
        using var agent = await factory.AddMemberAsync(owner);
        var ticket = await owner.OpenTicketAsync();
        await owner.Http.PatchAsJsonAsync($"/api/tickets/{ticket.Number}", new { assigneeId = agent.Id });
        await owner.Http.PatchAsJsonAsync($"/api/tickets/{ticket.Number}", new { status = "Solved" });
        await owner.ChangePlanAsync("Team");

        var page = await owner.Http.GetFromJsonAsync<AuditPageDto>("/api/audit", TestApi.Json);
        Assert.Equal(
            new[] { "plan.changed", "ticket.status_changed", "ticket.assigned", "member.joined", "member.invited" },
            page!.Items.Select(e => e.Action));
        Assert.Equal("Ayşe Yılmaz", page.Items[0].ActorName);
        Assert.Equal("Free → Team", page.Items[0].Details);

        var first = await owner.Http.GetFromJsonAsync<AuditPageDto>("/api/audit?limit=2", TestApi.Json);
        var second = await owner.Http.GetFromJsonAsync<AuditPageDto>($"/api/audit?limit=2&before={first!.Next}", TestApi.Json);
        Assert.Equal("ticket.assigned", second!.Items[0].Action);

        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Http.GetAsync("/api/audit")).StatusCode);
    }
}
