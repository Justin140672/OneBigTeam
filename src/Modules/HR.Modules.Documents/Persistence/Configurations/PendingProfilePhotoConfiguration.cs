using HR.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Documents.Persistence.Configurations;

internal sealed class PendingProfilePhotoConfiguration : IEntityTypeConfiguration<PendingProfilePhoto>
{
    public void Configure(EntityTypeBuilder<PendingProfilePhoto> builder)
    {
        builder.ToTable("pending_profile_photos");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(p => p.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(p => p.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(p => p.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(p => p.FileSize)
            .HasColumnName("file_size")
            .IsRequired();

        builder.Property(p => p.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(p => p.UploadedBy)
            .HasColumnName("uploaded_by")
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(p => p.ScanStatus)
            .HasColumnName("scan_status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.ScanCompletedAt)
            .HasColumnName("scan_completed_at");

        builder.Property(p => p.ScanAttemptCount)
            .HasColumnName("scan_attempt_count")
            .IsRequired();

        builder.Property(p => p.ScanFailureReason)
            .HasColumnName("scan_failure_reason")
            .HasMaxLength(500);

        builder.HasIndex(p => new { p.CompanyId, p.EmployeeId });
        builder.HasIndex(p => p.EmployeeId).IsUnique();
    }
}
