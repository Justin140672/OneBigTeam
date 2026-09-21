using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class InvitationBatchRecipientConfiguration : IEntityTypeConfiguration<InvitationBatchRecipient>
{
    public void Configure(EntityTypeBuilder<InvitationBatchRecipient> builder)
    {
        builder.ToTable("invitation_batch_recipients");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(r => r.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(r => r.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(r => r.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(r => r.InviteId).HasColumnName("invite_id");
        builder.Property(r => r.ProcessedAt).HasColumnName("processed_at");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(r => new { r.BatchId, r.Status });
    }
}
