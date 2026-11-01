using Helpdesk.Core.Tenants;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Infrastructure.Persistence;

/// <summary>
/// Accounts, organizations and memberships are shared tables; everything else belongs to one organization and is
/// filtered by the request's organization here, and again by row-level security in PostgreSQL.
/// </summary>
public sealed class HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options, TenantContext tenant) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<Comment> Comments => Set<Comment>();

    /// <summary>The organization the filters use; read per query, so it may be set after the context was created.</summary>
    internal Guid? CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HelpdeskDbContext).Assembly);
        modelBuilder.Entity<Ticket>().HasQueryFilter(t => t.TenantId == CurrentTenantId);
        modelBuilder.Entity<Comment>().HasQueryFilter(c => c.TenantId == CurrentTenantId);
    }
}
