using Helpdesk.Core.Tenants;
using Helpdesk.Core.Users;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Infrastructure.Persistence;

public sealed class HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HelpdeskDbContext).Assembly);
}
