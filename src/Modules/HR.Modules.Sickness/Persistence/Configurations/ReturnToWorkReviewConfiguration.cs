using HR.Modules.Sickness.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Sickness.Persistence.Configurations;

internal sealed class ReturnToWorkReviewConfiguration : IEntityTypeConfiguration<ReturnToWorkReview>
{
    public void Configure(EntityTypeBuilder<ReturnToWorkReview> builder)
    {
        builder.ToTable("return_to_work_reviews");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(r => r.SicknessRecordId)
            .HasColumnName("sickness_record_id")
            .IsRequired();

        builder.Property(r => r.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(r => r.DueDate)
            .HasColumnName("due_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(r => r.ReviewedBy)
            .HasColumnName("reviewed_by");

        builder.Property(r => r.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(r => r.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne<SicknessRecord>()
            .WithMany()
            .HasForeignKey(r => r.SicknessRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.CompanyId);
        builder.HasIndex(r => r.SicknessRecordId);
        builder.HasIndex(r => new { r.CompanyId, r.Status });
    }
}
