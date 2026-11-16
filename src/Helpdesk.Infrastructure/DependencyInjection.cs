using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Storage;
using Helpdesk.Infrastructure.Tenancy;

namespace Helpdesk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHelpdeskPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddDbContext<HelpdeskDbContext>((provider, options) =>
        {
            ConfigureDbContext(options, connectionString);
            options.AddInterceptors(provider.GetRequiredService<TenantConnectionInterceptor>());
        });
        return services;
    }

    /// <summary>Attachments on a local disk or volume under <paramref name="root"/>.</summary>
    public static IServiceCollection AddHelpdeskStorage(this IServiceCollection services, string root) =>
        services.AddSingleton<IFileStore>(new LocalFileStore(root));

    /// <summary>
    /// Applies migrations as the database owner. The API itself connects as the application role, which is subject
    /// to row-level security; without a separate migrations connection the API's own connection is used.
    /// </summary>
    /// <param name="appRolePassword">
    /// When given, the application role is allowed to sign in with this password: for a hosted database where no
    /// start-up script creates the role, such as the public demo's.
    /// </param>
    public static async Task MigrateAsync(
        IServiceProvider services, string? ownerConnectionString, string? appRolePassword = null, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        if (string.IsNullOrWhiteSpace(ownerConnectionString))
        {
            await scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>().Database.MigrateAsync(ct);
            return;
        }

        var options = new DbContextOptionsBuilder<HelpdeskDbContext>();
        ConfigureDbContext(options, ownerConnectionString);
        await using var owner = new HelpdeskDbContext(options.Options, new TenantContext());
        await owner.Database.MigrateAsync(ct);

        if (!string.IsNullOrEmpty(appRolePassword))
        {
            // ALTER ROLE takes no parameters, so the password travels in a session setting and format() quotes it.
            await owner.Database.OpenConnectionAsync(ct);
            await owner.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('helpdesk.app_role_password', {appRolePassword}, false)", ct);
            await owner.Database.ExecuteSqlRawAsync(
                """
                DO $$
                BEGIN
                    EXECUTE format('ALTER ROLE %I LOGIN PASSWORD %L', 'helpdesk_app', current_setting('helpdesk.app_role_password'));
                END
                $$;
                RESET helpdesk.app_role_password;
                """,
                ct);
        }
    }

    /// <summary>
    /// The application role's connection: the owner's server and database, signed in as the application role.
    /// </summary>
    public static string AppRoleConnectionString(string ownerConnectionString, string appRolePassword) =>
        new Npgsql.NpgsqlConnectionStringBuilder(ownerConnectionString)
        {
            Username = TenantIsolationSql.AppRole,
            Password = appRolePassword,
        }.ConnectionString;

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
