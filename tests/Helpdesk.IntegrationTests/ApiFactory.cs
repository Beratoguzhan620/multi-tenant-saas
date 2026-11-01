using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Helpdesk.IntegrationTests;

/// <summary>Boots the real API against a throwaway PostgreSQL container shared by the whole collection.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-tests-signing-key-0123456789abcdef";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public const string AppPassword = "app-role-test-password";

    /// <summary>The owner: creates the schema, and bypasses row-level security as a superuser.</summary>
    public string OwnerConnectionString => _database.GetConnectionString();

    /// <summary>What the API uses: the application role, which row-level security binds.</summary>
    public string AppConnectionString => new NpgsqlConnectionStringBuilder(OwnerConnectionString)
    {
        Username = "helpdesk_app",
        Password = AppPassword,
    }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Helpdesk", AppConnectionString);
        builder.UseSetting("ConnectionStrings:Migrations", OwnerConnectionString);
        builder.UseSetting("Public:RequestsPerMinute", "100000");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Auth:AttemptsPerMinute", "100000");
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        // What a deployment does once (docker-compose runs the same in an init script): the application role
        // exists and can sign in; the migrations grant it access to the tables.
        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE ROLE helpdesk_app LOGIN PASSWORD '{AppPassword}'", connection);
        await command.ExecuteNonQueryAsync();
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
