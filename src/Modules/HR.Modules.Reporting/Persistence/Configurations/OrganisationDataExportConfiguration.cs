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

        builder.Property(e => e.AttemptCount)
            .HasColumnName("attempt_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.LastAttemptAt)
            .HasColumnName("last_attempt_at");

        builder.Property(e => e.MissingDocumentCount)
            .HasColumnName("missing_document_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.AttemptFilesCleanedAt)
            .HasColumnName("attempt_files_cleaned_at");

        builder.Property(e => e.ArtefactCleanupNextAttemptAt)
            .HasColumnName("artefact_cleanup_next_attempt_at");

        builder.Property(e => e.ArtefactCleanupAttemptCount)
            .HasColumnName("artefact_cleanup_attempt_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.LateUploadRecheckNextAt)
            .HasColumnName("late_upload_recheck_next_at");

        builder.Property(e => e.LateUploadRecheckAttemptCount)
            .HasColumnName("late_upload_recheck_attempt_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.LeaseOwnerToken)
            .HasColumnName("lease_owner_token");

        builder.Property(e => e.LeaseAcquiredAt)
            .HasColumnName("lease_acquired_at");

        builder.Property(e => e.LeaseExpiresAt)
            .HasColumnName("lease_expires_at");

        builder.Property(e => e.Version)
            .HasColumnName("version")
            .HasDefaultValue(1)
            .IsConcurrencyToken()
            .IsRequired();

        // Reporting's DbContext has no tenant/current-user ctor dependency, so there is no global
        // query filter here — every handler/reader query filters company_id explicitly. These
        // indexes back those access paths.
        builder.HasIndex(e => new { e.CompanyId, e.Status });
        builder.HasIndex(e => new { e.CompanyId, e.RequestedAt });

        // Follow-up I: backs the retryable artefact-cleanup job's candidate scan
        // (terminal exports whose orphan attempt archives have not yet been swept).
        builder.HasIndex(e => new { e.Status, e.AttemptFilesCleanedAt });

        // Ticket 3K: back the durable cleanup cursor scan (eligible-now candidates, oldest first).
        builder.HasIndex(e => new { e.AttemptFilesCleanedAt, e.ArtefactCleanupNextAttemptAt });

        // Ticket 3K: back the durable late-upload straggler recheck cursor scan.
        builder.HasIndex(e => new { e.AttemptFilesCleanedAt, e.LateUploadRecheckNextAt });

        // Ticket 3: at most one active (Pending/InProgress) export per company. This is the race
        // backstop for the check-then-insert in RequestOrganisationDataExportHandler — concurrent
        // requests or retries can never create a second conflicting active export.
        builder.HasIndex(e => e.CompanyId)
            .HasDatabaseName("ix_organisation_data_exports_active_per_company")
            .HasFilter("status IN ('Pending', 'InProgress')")
            .IsUnique();
    }
}
