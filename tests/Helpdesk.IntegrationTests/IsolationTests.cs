using System.Net;
using System.Net.Http.Json;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Tenancy;
using Helpdesk.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Helpdesk.IntegrationTests;

/// <summary>
/// Two organizations side by side. The first half goes through the API; the second talks to PostgreSQL directly
/// as the application role, to show the database refuses even when the code forgets to filter.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class IsolationTests(ApiFactory factory)
{
    [Fact]
    public async Task Both_organizations_have_a_1001_and_each_sees_only_its_own()
    {
        using var kargo = await factory.SignUpAsync(organization: "Kargo");
        using var market = await factory.SignUpAsync(organization: "Market");

        var a = await kargo.OpenTicketAsync("Kargo talebi");
        var b = await market.OpenTicketAsync("Market talebi");

        Assert.Equal(1001, a.Number);
        Assert.Equal(1001, b.Number);
        Assert.Equal("Kargo talebi", (await kargo.Http.GetFromJsonAsync<TicketDto>("/api/tickets/1001", TestApi.Json))!.Subject);
        Assert.Equal("Market talebi", (await market.Http.GetFromJsonAsync<TicketDto>("/api/tickets/1001", TestApi.Json))!.Subject);
        Assert.Equal(new[] { "Kargo talebi" }, (await kargo.Http.GetFromJsonAsync<TicketPageDto>("/api/tickets", TestApi.Json))!.Items.Select(t => t.Subject));
    }

    [Fact]
    public async Task Another_organizations_ticket_cannot_be_read_changed_or_answered()
    {
        using var kargo = await factory.SignUpAsync();
        using var market = await factory.SignUpAsync();
        await kargo.OpenTicketAsync();
        await kargo.OpenTicketAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await market.Http.GetAsync("/api/tickets/1002")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await market.Http.PatchAsJsonAsync("/api/tickets/1002", new { status = "Closed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await market.Http.PostAsJsonAsync("/api/tickets/1002/comments", new { body = "hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await market.Http.GetAsync("/api/tickets/1002/comments")).StatusCode);
        Assert.Equal("Open", (await kargo.Http.GetFromJsonAsync<TicketDto>("/api/tickets/1002", TestApi.Json))!.Status);
    }

    [Fact]
    public async Task Tickets_cannot_be_assigned_to_someone_from_another_organization()
    {
        using var kargo = await factory.SignUpAsync();
        using var market = await factory.SignUpAsync();
        var ticket = await kargo.OpenTicketAsync();

        var response = await kargo.Http.PatchAsJsonAsync($"/api/tickets/{ticket.Number}", new { assigneeId = market.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_customer_key_only_works_on_its_own_organizations_form()
    {
        using var kargo = await factory.SignUpAsync();
        using var market = await factory.SignUpAsync();
        var created = await factory.OpenPublicTicketAsync(kargo.Slug);
        await factory.OpenPublicTicketAsync(market.Slug);
        using var anonymous = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/public/{market.Slug}/tickets/{created.Number}");
        request.Headers.Add("X-Ticket-Key", created.AccessKey);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Without_an_organization_the_app_role_sees_no_tickets_at_all()
    {
        using var kargo = await factory.SignUpAsync();
        await kargo.OpenTicketAsync();

        await using var connection = await OpenAsAppAsync(tenant: null);

        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM tickets"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM ticket_comments"));
    }

    [Fact]
    public async Task With_an_organization_the_app_role_sees_only_its_rows_even_unfiltered()
    {
        using var kargo = await factory.SignUpAsync();
        using var market = await factory.SignUpAsync();
        await kargo.OpenTicketAsync();
        await kargo.OpenTicketAsync();
        await market.OpenTicketAsync();

        await using var connection = await OpenAsAppAsync(kargo.OrganizationId);

        Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM tickets"));
        Assert.Equal(0L, await ScalarAsync(connection, $"SELECT count(*) FROM tickets WHERE tenant_id = '{market.OrganizationId}'"));
        Assert.Equal(0, await ExecuteAsync(connection, $"UPDATE tickets SET subject = 'x' WHERE tenant_id = '{market.OrganizationId}'"));
        Assert.Equal(0, await ExecuteAsync(connection, $"DELETE FROM ticket_comments WHERE tenant_id = '{market.OrganizationId}'"));
    }

    [Fact]
    public async Task The_app_role_cannot_write_a_row_into_another_organization()
    {
        using var kargo = await factory.SignUpAsync();
        using var market = await factory.SignUpAsync();
        await using var connection = await OpenAsAppAsync(kargo.OrganizationId);

        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"""
            INSERT INTO tickets (id, tenant_id, number, subject, status, priority, channel, requester_name, requester_email, access_key_hash, created_at, updated_at)
            VALUES (gen_random_uuid(), '{market.OrganizationId}', 1, 's', 'Open', 'Normal', 'Web', 'n', 'e@example.com', '\x00', now(), now())
            """));

        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task The_app_role_can_neither_bypass_nor_switch_off_row_level_security()
    {
        await using var connection = await OpenAsAppAsync(tenant: null);

        Assert.False((bool)(await ScalarAsync(connection, "SELECT rolbypassrls OR rolsuper FROM pg_roles WHERE rolname = current_user"))!);
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, "ALTER TABLE tickets DISABLE ROW LEVEL SECURITY"));
        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task Ignoring_the_query_filter_still_returns_only_the_current_organization()
    {
        using var kargo = await factory.SignUpAsync();
        using var market = await factory.SignUpAsync();
        await kargo.OpenTicketAsync();
        await market.OpenTicketAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(kargo.OrganizationId, kargo.Id, Core.Tenants.Role.Owner);
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();

        var tenants = await db.Tickets.IgnoreQueryFilters().Select(t => t.TenantId).Distinct().ToListAsync();

        Assert.Equal(new[] { kargo.OrganizationId }, tenants);
    }

    [Fact]
    public async Task A_pooled_connection_does_not_keep_the_previous_organization()
    {
        using var kargo = await factory.SignUpAsync();
        await kargo.OpenTicketAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(kargo.OrganizationId, kargo.Id, Core.Tenants.Role.Owner);
            Assert.True(await scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>().Tickets.AnyAsync());
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
            Assert.Equal(0, await db.Tickets.IgnoreQueryFilters().CountAsync());
        }
    }

    private async Task<NpgsqlConnection> OpenAsAppAsync(Guid? tenant)
    {
        var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();
        if (tenant is not null)
        {
            await using var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, false)", connection);
            set.Parameters.AddWithValue("t", tenant.Value.ToString());
            await set.ExecuteNonQueryAsync();
        }

        return connection;
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }
}
