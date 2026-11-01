using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helpdesk.IntegrationTests.Support;

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record OrganizationDto(Guid Id, string Name, string Slug, string Plan, string Role);

public sealed record SessionDto(string AccessToken, DateTimeOffset ExpiresAt, UserDto User, OrganizationDto? Organization, List<OrganizationDto> Organizations);

/// <summary>A signed-in person with an HTTP client that carries the token for one organization.</summary>
public sealed class Person(ApiFactory factory, SessionDto session) : IDisposable
{
    public SessionDto Session { get; private set; } = session;

    public HttpClient Http { get; private set; } = Client(factory, session.AccessToken);

    public Guid Id => Session.User.Id;

    public string Email => Session.User.Email;

    public Guid OrganizationId => Session.Organization!.Id;

    public string Slug => Session.Organization!.Slug;

    /// <summary>Uses a new session, for example after switching organization.</summary>
    public void Use(SessionDto session)
    {
        Http.Dispose();
        Session = session;
        Http = Client(factory, session.AccessToken);
    }

    public void Dispose() => Http.Dispose();

    private static HttpClient Client(ApiFactory factory, string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}

public static class TestApi
{
    public const string Password = "correct-horse-battery";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    /// <summary>Signs up a new organization and returns its owner.</summary>
    public static async Task<Person> SignUpAsync(this ApiFactory factory, string organization = "Kargo Hızlı", string name = "Ayşe Yılmaz")
    {
        using var anonymous = factory.CreateClient();
        var slug = Unique("org");
        var response = await anonymous.PostAsJsonAsync("/api/auth/signup", new
        {
            organizationName = organization,
            slug,
            displayName = name,
            email = $"{slug}@example.com",
            password = Password,
        });
        response.EnsureSuccessStatusCode();
        return new Person(factory, await response.ReadAsync<SessionDto>());
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}

public sealed record PersonRefDto(Guid Id, string Name);

public sealed record TicketDto(
    long Number, string Subject, string Status, string Priority, string Channel,
    string RequesterName, string RequesterEmail, PersonRefDto? Assignee, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record TicketPageDto(List<TicketDto> Items, int Total, int Page, int PageSize);

public sealed record CommentDto(Guid Id, string AuthorName, bool ByTeam, bool IsInternal, string Body, DateTimeOffset CreatedAt);

public sealed record PublicCreatedDto(long Number, string AccessKey);

public sealed record PublicTicketDto(long Number, string Subject, string Status, DateTimeOffset CreatedAt, List<CommentDto> Messages);

public sealed record MemberDto(Guid Id, string DisplayName, string Email, string Role, DateTimeOffset JoinedAt);

public static class Tickets
{
    public static async Task<TicketDto> OpenTicketAsync(this Person person, string subject = "Kargom gelmedi", string email = "musteri@example.com")
    {
        var response = await person.Http.PostAsJsonAsync("/api/tickets", new
        {
            subject,
            requesterName = "Mehmet Kaya",
            requesterEmail = email,
            message = "Siparişim üç gündür yolda görünüyor.",
        });
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<TicketDto>();
    }

    public static async Task<PublicCreatedDto> OpenPublicTicketAsync(this ApiFactory factory, string slug, string subject = "Fatura sorusu")
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync($"/api/public/{slug}/tickets", new
        {
            name = "Zeynep Demir",
            email = "zeynep@example.com",
            subject,
            message = "Geçen ayın faturası iki kez kesilmiş.",
        });
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<PublicCreatedDto>();
    }
}

public sealed record PlanDto(string Id, string Name, int Seats, int? TicketsPerMonth, long StorageBytes, int RequestsPerMinute, int PriceUsd);

public sealed record UsageDto(PlanDto Plan, int Seats, long TicketsThisMonth, long StorageBytes);

public sealed record InvitationDto(Guid Id, string Email, string Role, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Expired);

public sealed record InvitationCreatedDto(InvitationDto Invitation, string Token);

public sealed record InvitationPreviewDto(string OrganizationName, string Email, string Role, bool Expired, bool HasAccount);

public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset CreatedAt);

public sealed record AuditEventDto(Guid Id, string ActorName, string Action, string Target, string? Details, DateTimeOffset At);

public sealed record AuditPageDto(List<AuditEventDto> Items, Guid? Next);

public static class Teams
{
    public static async Task<InvitationCreatedDto> InviteAsync(this Person admin, string email, string role = "Agent")
    {
        var response = await admin.Http.PostAsJsonAsync("/api/invitations", new { email, role });
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<InvitationCreatedDto>();
    }

    /// <summary>Invites a new person and has them join with a new account; returns them signed in to the organization.</summary>
    public static async Task<Person> AddMemberAsync(this ApiFactory factory, Person admin, string role = "Agent", string name = "Can Öztürk")
    {
        var email = $"{TestApi.Unique("member")}@example.com";
        var invitation = await admin.InviteAsync(email, role);
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/auth/join", new { token = invitation.Token, displayName = name, password = TestApi.Password });
        response.EnsureSuccessStatusCode();
        return new Person(factory, await response.ReadAsync<SessionDto>());
    }

    public static async Task ChangePlanAsync(this Person owner, string plan) =>
        (await owner.Http.PutAsJsonAsync("/api/organization/plan", new { plan })).EnsureSuccessStatusCode();

    public static async Task<UsageDto> UsageAsync(this Person person) =>
        (await person.Http.GetFromJsonAsync<UsageDto>("/api/organization/usage", TestApi.Json))!;
}
