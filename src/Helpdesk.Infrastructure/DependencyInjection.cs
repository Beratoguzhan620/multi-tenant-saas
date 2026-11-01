using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Helpdesk.Infrastructure.Persistence;
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

    /// <summary>
    /// Applies migrations as the database owner. The API itself connects as the application role, which is subject
    /// to row-level security; without a separate migrations connection the API's own connection is used.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, string? ownerConnectionString, CancellationToken ct = default)
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
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
