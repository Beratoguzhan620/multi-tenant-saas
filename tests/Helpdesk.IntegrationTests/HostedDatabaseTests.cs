using System.Net;
using Helpdesk.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Helpdesk.IntegrationTests;

/// <summary>
/// The public demo's set-up: a hosted database gives one owner connection and no start-up script, so nothing has
/// created the application role beforehand. The API is given the owner connection and a password for the role.
/// </summary>
public sealed class HostedDatabaseTests : IAsyncLifetime
{
    private const string RolePassword = "generated-role-password-for-the-test";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _database.StartAsync();

    public Task DisposeAsync() => _database.DisposeAsync().AsTask();

    [Fact]
    public async Task With_only_the_owner_connection_the_api_signs_in_as_the_application_role()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Migrations", _database.GetConnectionString());
            builder.UseSetting("Database:AppRolePassword", RolePassword);
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("Jwt:SigningKey", ApiFactory.SigningKey);
            builder.UseSetting("Storage:Path", Path.Combine(Path.GetTempPath(), $"helpdesk-hosted-{Guid.NewGuid():N}"));
        });
        using var client = factory.CreateClient();

        var ready = await client.GetAsync("/health/ready");

        await using var connection = new NpgsqlConnection(
            DependencyInjection.AppRoleConnectionString(_database.GetConnectionString(), RolePassword));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user", connection);
        var bypasses = (bool)(await command.ExecuteScalarAsync())!;

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("helpdesk_app", connection.UserName);
        Assert.False(bypasses);
    }
}
