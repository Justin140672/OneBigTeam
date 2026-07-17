using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Documents.Persistence.Configurations;

internal sealed class SharedCompanyDocumentReviewHistoryConfiguration : IEntityTypeConfiguration<SharedCompanyDocumentReviewHistory>
{
    public void Configure(EntityTypeBuilder<SharedCompanyDocumentReviewHistory> builder)
    {
        builder.ToTable("shared_company_document_review_histories");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(h => h.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(h => h.SharedCompanyDocumentId)
            .HasColumnName("shared_company_document_id")
            .IsRequired();

        builder.Property(h => h.ReviewDate)
            .HasColumnName("review_date")
            .IsRequired();

        builder.Property(h => h.ReviewedByEmployeeId)
            .HasColumnName("reviewed_by_employee_id")
            .IsRequired();

        builder.Property(h => h.ReviewNotes)
            .HasColumnName("review_notes")
            .HasMaxLength(1000);

        builder.Property(h => h.PreviousReviewDate)
            .HasColumnName("previous_review_date");

        builder.Property(h => h.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasOne<SharedCompanyDocument>()
            .WithMany()
            .HasForeignKey(h => h.SharedCompanyDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(h => h.CompanyId);
        builder.HasIndex(h => new { h.SharedCompanyDocumentId, h.ReviewDate });
    }
}
