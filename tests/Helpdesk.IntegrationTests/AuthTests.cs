using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Helpdesk.IntegrationTests.Support;

namespace Helpdesk.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Signing_up_creates_the_organization_with_its_owner_on_the_free_plan()
    {
        using var owner = await factory.SignUpAsync("Kargo Hızlı");

        Assert.Equal("Kargo Hızlı", owner.Session.Organization!.Name);
        Assert.Equal("Free", owner.Session.Organization.Plan);
        Assert.Equal("Owner", owner.Session.Organization.Role);
        Assert.Single(owner.Session.Organizations);
        var organization = await owner.Http.GetFromJsonAsync<Dictionary<string, object>>("/api/organization");
        Assert.Equal(owner.Slug, organization!["slug"].ToString());
    }

    [Fact]
    public async Task Addresses_and_emails_are_taken_once()
    {
        using var owner = await factory.SignUpAsync();
        using var client = factory.CreateClient();

        var sameSlug = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            organizationName = "Başka", slug = owner.Slug.ToUpperInvariant(), displayName = "Mehmet", email = $"{TestApi.Unique("m")}@example.com", password = TestApi.Password,
        });
        var sameEmail = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            organizationName = "Başka", slug = TestApi.Unique("org"), displayName = "Mehmet", email = owner.Email, password = TestApi.Password,
        });

        Assert.Equal(HttpStatusCode.Conflict, sameSlug.StatusCode);
        Assert.Equal("slug_taken", await sameSlug.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
        Assert.Equal("email_taken", await sameEmail.ProblemCodeAsync());
    }

    [Fact]
    public async Task Bad_sign_ups_name_every_problem()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/signup", new { organizationName = "", slug = "", displayName = "A", email = "nope", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var field in new[] { "organizationName", "slug", "displayName", "email", "password" })
        {
            Assert.Contains(field, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Login_picks_the_named_organization_and_wrong_details_get_one_answer()
    {
        using var owner = await factory.SignUpAsync("Birinci");
        var second = await owner.Http.PostAsJsonAsync("/api/organizations", new { name = "İkinci", slug = TestApi.Unique("org") });
        var secondSlug = (await second.ReadAsync<SessionDto>()).Organization!.Slug;
        using var client = factory.CreateClient();

        var first = await (await client.PostAsJsonAsync("/api/auth/login", new { email = owner.Email, password = TestApi.Password })).ReadAsync<SessionDto>();
        var named = await (await client.PostAsJsonAsync("/api/auth/login", new { email = owner.Email, password = TestApi.Password, organization = secondSlug })).ReadAsync<SessionDto>();
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { email = owner.Email, password = "wrong-password-1" });
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = TestApi.Password });
        var notMine = await client.PostAsJsonAsync("/api/auth/login", new { email = owner.Email, password = TestApi.Password, organization = "someone-else" });

        Assert.Equal(owner.OrganizationId, first.Organization!.Id);
        Assert.Equal(secondSlug, named.Organization!.Slug);
        Assert.Equal(2, named.Organizations.Count);
        Assert.Equal("invalid_credentials", await wrongPassword.ProblemCodeAsync());
        Assert.Equal("invalid_credentials", await unknown.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, notMine.StatusCode);
    }

    [Fact]
    public async Task Switching_works_only_between_your_own_organizations()
    {
        using var owner = await factory.SignUpAsync("Birinci");
        using var stranger = await factory.SignUpAsync("Yabancı");
        var created = await (await owner.Http.PostAsJsonAsync("/api/organizations", new { name = "İkinci", slug = TestApi.Unique("org") })).ReadAsync<SessionDto>();

        var back = await owner.Http.PostAsJsonAsync("/api/auth/switch", new { organizationId = owner.OrganizationId });
        var intoStranger = await owner.Http.PostAsJsonAsync("/api/auth/switch", new { organizationId = stranger.OrganizationId });

        Assert.Equal(owner.OrganizationId, (await back.ReadAsync<SessionDto>()).Organization!.Id);
        Assert.NotEqual(owner.OrganizationId, created.Organization!.Id);
        Assert.Equal(HttpStatusCode.NotFound, intoStranger.StatusCode);
    }

    [Fact]
    public async Task A_token_for_one_organization_cannot_be_pointed_at_another()
    {
        using var owner = await factory.SignUpAsync();
        using var stranger = await factory.SignUpAsync();

        // The stranger's token names the stranger's organization; there is no way to ask for the owner's.
        var mine = await stranger.Http.GetFromJsonAsync<Dictionary<string, object>>("/api/organization");

        Assert.Equal(stranger.Slug, mine!["slug"].ToString());
        Assert.NotEqual(owner.Slug, mine["slug"].ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not.a.jwt")]
    public async Task Requests_without_a_valid_token_are_unauthorized(string? token)
    {
        using var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/organization")).StatusCode);
    }

    [Fact]
    public async Task Renaming_needs_an_admin()
    {
        using var owner = await factory.SignUpAsync();

        var renamed = await owner.Http.PatchAsJsonAsync("/api/organization", new { name = "Yeni Ad" });

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Yeni Ad", (await renamed.ReadAsync<Dictionary<string, object>>())["name"].ToString());
    }
}
