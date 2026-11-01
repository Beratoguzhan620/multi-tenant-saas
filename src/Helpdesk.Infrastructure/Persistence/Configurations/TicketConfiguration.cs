using Helpdesk.Core.Tenants;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Subject).HasMaxLength(Ticket.SubjectMaxLength);
        builder.Property(t => t.RequesterName).HasMaxLength(Ticket.NameMaxLength);
        builder.Property(t => t.RequesterEmail).HasMaxLength(Ticket.EmailMaxLength);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Priority).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Channel).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.AccessKeyHash).HasMaxLength(32);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.SetNull);

        // Every query starts from the organization: its numbers, its newest tickets, its open ones.
        builder.HasIndex(t => new { t.TenantId, t.Number }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.Status, t.UpdatedAt });
        builder.HasIndex(t => new { t.TenantId, t.AssigneeId });
    }
}

internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("ticket_comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.AuthorName).HasMaxLength(100);
        builder.Property(c => c.Body).HasMaxLength(Comment.BodyMaxLength);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.TenantId, c.TicketId, c.CreatedAt });
    }
}
