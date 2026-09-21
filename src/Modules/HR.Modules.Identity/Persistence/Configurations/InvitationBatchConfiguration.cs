using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class InvitationBatchConfiguration : IEntityTypeConfiguration<InvitationBatch>
{
    public void Configure(EntityTypeBuilder<InvitationBatch> builder)
    {
        builder.ToTable("invitation_batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(b => b.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(b => b.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(b => b.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(b => b.StartedAt).HasColumnName("started_at");
        builder.Property(b => b.CompletedAt).HasColumnName("completed_at");
        builder.Property(b => b.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);

        builder.HasIndex(b => b.CompanyId);

        // Partial unique index: prevents a double-clicked/retried queue submission (with the same
        // supplied Idempotency-Key) from creating a second batch for the same company.
        builder.HasIndex(b => new { b.CompanyId, b.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ix_invitation_batches_company_id_idempotency_key")
            .HasFilter("idempotency_key IS NOT NULL");
    }
}
