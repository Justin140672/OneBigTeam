using HR.Modules.Reporting.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Reporting.Persistence.Configurations;

internal sealed class OrganisationDataExportConfiguration : IEntityTypeConfiguration<OrganisationDataExport>
{
    public void Configure(EntityTypeBuilder<OrganisationDataExport> builder)
    {
        builder.ToTable("organisation_data_exports");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(e => e.RequestedByUserId)
            .HasColumnName("requested_by_user_id");

        builder.Property(e => e.RequestedByDisplayName)
            .HasColumnName("requested_by_display_name");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.RequestedAt)
            .HasColumnName("requested_at")
            .IsRequired();

        builder.Property(e => e.StartedAt)
            .HasColumnName("started_at");

        builder.Property(e => e.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(e => e.ExpiresAt)
            .HasColumnName("expires_at");

        builder.Property(e => e.StorageKey)
            .HasColumnName("storage_key");

        builder.Property(e => e.FileSizeBytes)
            .HasColumnName("file_size_bytes");

        builder.Property(e => e.FailureReason)
            .HasColumnName("failure_reason");

        builder.Property(e => e.DownloadCount)
            .HasColumnName("download_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.LastDownloadedAt)
            .HasColumnName("last_downloaded_at");

        builder.Property(e => e.LastDownloadedByUserId)
            .HasColumnName("last_downloaded_by_user_id");

        // Reporting's DbContext has no tenant/current-user ctor dependency, so there is no global
        // query filter here — every handler/reader query filters company_id explicitly. These
        // indexes back those access paths.
        builder.HasIndex(e => new { e.CompanyId, e.Status });
        builder.HasIndex(e => new { e.CompanyId, e.RequestedAt });
    }
}
