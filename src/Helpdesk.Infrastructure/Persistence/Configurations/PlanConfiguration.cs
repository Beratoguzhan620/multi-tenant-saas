using Helpdesk.Core.Audit;
using Helpdesk.Core.Tenants;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Infrastructure.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Infrastructure.Persistence.Configurations;

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Email).HasMaxLength(Ticket.EmailMaxLength);
        builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(10);
        builder.Property(i => i.SecretHash).HasMaxLength(32);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.InvitedBy).OnDelete(DeleteBehavior.Cascade);

        // One open invitation per address and organization; inviting again replaces it.
        builder.HasIndex(i => new { i.TenantId, i.Email }).IsUnique();
        builder.HasIndex(i => i.SecretHash).IsUnique();
    }
}

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("ticket_attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.FileName).HasMaxLength(Attachment.FileNameMaxLength);
        builder.Property(a => a.ContentType).HasMaxLength(100);
        builder.Property(a => a.StorageKey).HasMaxLength(65);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UploadedBy).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(a => new { a.TenantId, a.TicketId });
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.ActorName).HasMaxLength(100);
        builder.Property(a => a.Action).HasMaxLength(40);
        builder.Property(a => a.Target).HasMaxLength(200);
        builder.Property(a => a.Details).HasMaxLength(500);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.TenantId, a.At, a.Id });
    }
}

internal sealed class UsageCounterConfiguration : IEntityTypeConfiguration<UsageCounter>
{
    public void Configure(EntityTypeBuilder<UsageCounter> builder)
    {
        builder.ToTable("usage_counters");
        builder.HasKey(c => new { c.TenantId, c.Metric, c.Period });
        builder.Property(c => c.Metric).HasMaxLength(20);
        builder.Property(c => c.Period).HasMaxLength(7);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
