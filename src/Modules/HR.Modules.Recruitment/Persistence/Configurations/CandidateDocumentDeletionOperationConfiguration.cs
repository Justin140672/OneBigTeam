using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class CandidateDocumentDeletionOperationConfiguration
    : IEntityTypeConfiguration<CandidateDocumentDeletionOperation>
{
    public void Configure(EntityTypeBuilder<CandidateDocumentDeletionOperation> builder)
    {
        builder.ToTable("candidate_document_deletion_operations");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(o => o.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(o => o.CandidateId).HasColumnName("candidate_id").IsRequired();
        builder.Property(o => o.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.Property(o => o.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(o => o.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(o => o.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(o => o.FailureReason).HasColumnName("failure_reason").HasMaxLength(2000);
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.CompletedAt).HasColumnName("completed_at");

        builder.HasIndex(o => o.CandidateId);
        builder.HasIndex(o => new { o.CompanyId, o.Status });
    }
}
