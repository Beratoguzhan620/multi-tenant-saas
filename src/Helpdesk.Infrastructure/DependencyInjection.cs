using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Helpdesk.Infrastructure.Persistence;

namespace Helpdesk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHelpdeskPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<HelpdeskDbContext>(options => ConfigureDbContext(options, connectionString));
        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
