using Helpdesk.Core.Tenants;
using Helpdesk.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(Tenant.NameMaxLength);
        builder.Property(t => t.Slug).HasMaxLength(40);
        builder.Property(t => t.Plan).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(t => t.Slug).IsUnique();
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");
        builder.HasKey(m => new { m.TenantId, m.UserId });
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(10);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        // "Which organizations am I in" starts from the user.
        builder.HasIndex(m => m.UserId);
    }
}
