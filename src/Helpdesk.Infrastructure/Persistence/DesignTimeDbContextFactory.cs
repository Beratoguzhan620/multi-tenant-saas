using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Helpdesk.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; generating migrations does not open a connection.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HelpdeskDbContext>
{
    public HelpdeskDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HelpdeskDbContext>();
        DependencyInjection.ConfigureDbContext(options, "Host=localhost;Database=helpdesk_design");
        return new HelpdeskDbContext(options.Options, new Tenancy.TenantContext());
    }
}
