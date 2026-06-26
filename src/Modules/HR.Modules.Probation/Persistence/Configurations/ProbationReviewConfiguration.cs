using HR.Modules.Probation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Probation.Persistence.Configurations;

internal sealed class ProbationReviewConfiguration : IEntityTypeConfiguration<ProbationReview>
{
    public void Configure(EntityTypeBuilder<ProbationReview> builder)
    {
        builder.ToTable("probation_reviews", "probation");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CompanyId).HasColumnName("company_id");
        builder.Property(r => r.ProbationRecordId).HasColumnName("probation_record_id");
        builder.Property(r => r.ReviewType)
            .HasColumnName("review_type")
            .HasMaxLength(30)
            .HasConversion<string>();
        builder.Property(r => r.DueDate).HasColumnName("due_date");
        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>();
        builder.Property(r => r.CompletedAt).HasColumnName("completed_at");
        builder.Property(r => r.CompletedByEmployeeId).HasColumnName("completed_by_employee_id");
        builder.Property(r => r.Outcome)
            .HasColumnName("outcome")
            .HasMaxLength(20)
            .HasConversion<string>();
        builder.Property(r => r.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(r => r.CompanyId).HasDatabaseName("IX_probation_reviews_company_id");
        builder.HasIndex(r => r.ProbationRecordId).HasDatabaseName("IX_probation_reviews_probation_record_id");
        builder.HasIndex(r => new { r.CompanyId, r.Status }).HasDatabaseName("IX_probation_reviews_company_id_status");
        builder.HasIndex(r => new { r.CompanyId, r.DueDate }).HasDatabaseName("IX_probation_reviews_company_id_due_date");
    }
}
