using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Helpdesk.IntegrationTests.Support;
using Npgsql;

namespace Helpdesk.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AttachmentTests(ApiFactory factory)
{
    [Fact]
    public async Task Files_are_uploaded_downloaded_and_deleted_with_their_storage()
    {
        using var owner = await factory.SignUpAsync();
        var ticket = await owner.OpenTicketAsync();

        var uploaded = await UploadAsync(owner, ticket.Number, "../../kargo-fişi.txt", "text/plain", "Takip no: 123");
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var attachment = await uploaded.ReadAsync<AttachmentDto>();
        Assert.Equal("kargo-fişi.txt", attachment.FileName);
        Assert.Equal(attachment.SizeBytes, (await owner.UsageAsync()).StorageBytes);

        var list = await owner.Http.GetFromJsonAsync<List<AttachmentDto>>($"/api/tickets/{ticket.Number}/attachments", TestApi.Json);
        Assert.Single(list!);

        var download = await owner.Http.GetAsync($"/api/attachments/{attachment.Id}");
        Assert.Equal("Takip no: 123", await download.Content.ReadAsStringAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.DeleteAsync($"/api/attachments/{attachment.Id}")).StatusCode);
        Assert.Equal(0, (await owner.UsageAsync()).StorageBytes);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Http.GetAsync($"/api/attachments/{attachment.Id}")).StatusCode);
    }

    [Fact]
    public async Task Another_organization_cannot_see_or_delete_a_file()
    {
        using var kargo = await factory.SignUpAsync();
        using var market = await factory.SignUpAsync();
        var ticket = await kargo.OpenTicketAsync();
        var attachment = await (await UploadAsync(kargo, ticket.Number, "a.txt", "text/plain", "gizli")).ReadAsync<AttachmentDto>();

        Assert.Equal(HttpStatusCode.NotFound, (await market.Http.GetAsync($"/api/attachments/{attachment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await market.Http.DeleteAsync($"/api/attachments/{attachment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await kargo.Http.GetAsync($"/api/attachments/{attachment.Id}")).StatusCode);
    }

    [Fact]
    public async Task Unsafe_types_are_refused()
    {
        using var owner = await factory.SignUpAsync();
        var ticket = await owner.OpenTicketAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(owner, ticket.Number, "x.html", "text/html", "<script>")).StatusCode);
        Assert.Equal(0, (await owner.UsageAsync()).StorageBytes);
    }

    [Fact]
    public async Task A_file_that_does_not_fit_the_plan_is_refused_and_takes_nothing()
    {
        using var owner = await factory.SignUpAsync();
        var ticket = await owner.OpenTicketAsync();
        const long almostFull = (100 * 1024 * 1024) - 5;
        await factory.ExecuteAsOwnerAsync($"""
            INSERT INTO usage_counters (tenant_id, metric, period, value) VALUES ('{owner.OrganizationId}', 'storage_bytes', 'all', {almostFull})
            """);

        var refused = await UploadAsync(owner, ticket.Number, "a.txt", "text/plain", "123456");
        Assert.Equal(HttpStatusCode.PaymentRequired, refused.StatusCode);
        Assert.Equal(almostFull, (await owner.UsageAsync()).StorageBytes);

        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(owner, ticket.Number, "b.txt", "text/plain", "12345")).StatusCode);
    }

    [Fact]
    public async Task Agents_delete_only_their_own_files()
    {
        using var owner = await factory.SignUpAsync();
        using var agent = await factory.AddMemberAsync(owner);
        var ticket = await owner.OpenTicketAsync();
        var ownersFile = await (await UploadAsync(owner, ticket.Number, "a.txt", "text/plain", "a")).ReadAsync<AttachmentDto>();
        var agentsFile = await (await UploadAsync(agent, ticket.Number, "b.txt", "text/plain", "b")).ReadAsync<AttachmentDto>();

        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Http.DeleteAsync($"/api/attachments/{ownersFile.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await agent.Http.DeleteAsync($"/api/attachments/{agentsFile.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_app_role_cannot_rewrite_the_audit_log()
    {
        using var owner = await factory.SignUpAsync();
        await owner.ChangePlanAsync("Team");

        await using var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();
        await using var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id', '{owner.OrganizationId}', false)", connection);
        await set.ExecuteNonQueryAsync();
        await using var update = new NpgsqlCommand("DELETE FROM audit_events", connection);

        var error = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());
        Assert.Equal("42501", error.SqlState);
    }

    private static async Task<HttpResponseMessage> UploadAsync(Person person, long number, string name, string type, string content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", name);
        return await person.Http.PostAsync($"/api/tickets/{number}/attachments", form);
    }
}
