using HR.Modules.Support.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Support.Persistence.Configurations;

internal sealed class SupportAttachmentPendingDeletionConfiguration
    : IEntityTypeConfiguration<SupportAttachmentPendingDeletion>
{
    public void Configure(EntityTypeBuilder<SupportAttachmentPendingDeletion> builder)
    {
        builder.ToTable("support_attachment_pending_deletions");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(d => d.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(d => d.LastFailureReason)
            .HasColumnName("last_failure_reason")
            .HasMaxLength(1000);

        builder.Property(d => d.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(d => d.LastAttemptAt)
            .HasColumnName("last_attempt_at");

        builder.Property(d => d.ResolvedAt)
            .HasColumnName("resolved_at");

        builder.HasIndex(d => d.ResolvedAt);

        // Security review finding #4 (P1): idempotency guard for unresolved deletion records — a
        // retried/duplicated cleanup attempt for the same storage key must not create a second
        // unresolved row; the retry job only ever needs one live record per key.
        builder.HasIndex(d => d.StorageKey)
            .HasDatabaseName("ix_support_attachment_pending_deletions_storage_key_unresolved")
            .IsUnique()
            .HasFilter("resolved_at IS NULL");
    }
}
