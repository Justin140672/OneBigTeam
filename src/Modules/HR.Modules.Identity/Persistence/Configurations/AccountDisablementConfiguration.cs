using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Identity.Persistence.Configurations;

internal sealed class AccountDisablementConfiguration : IEntityTypeConfiguration<AccountDisablement>
{
    public void Configure(EntityTypeBuilder<AccountDisablement> builder)
    {
        builder.ToTable("account_disablements");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(d => d.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(d => d.ApplicationUserId)
            .HasColumnName("application_user_id")
            .IsRequired();

        builder.Property(d => d.EmployeeId)
            .HasColumnName("employee_id")
            .IsRequired();

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(d => d.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(d => d.LastAttemptAt)
            .HasColumnName("last_attempt_at");

        builder.Property(d => d.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(500);

        builder.Property(d => d.RequestedAt)
            .HasColumnName("requested_at")
            .IsRequired();

        builder.Property(d => d.ProcessedAt)
            .HasColumnName("processed_at");

        // Ticket 19 (P2): claim/lease + terminal-failure columns.
        builder.Property(d => d.Version)
            .HasColumnName("version")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(d => d.ClaimedBy)
            .HasColumnName("claimed_by");

        builder.Property(d => d.LeaseExpiresAt)
            .HasColumnName("lease_expires_at");

        builder.Property(d => d.IsTerminallyFailed)
            .HasColumnName("is_terminally_failed")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(d => d.LastRetriedByActorId)
            .HasColumnName("last_retried_by_actor_id");

        builder.Property(d => d.LastRetryReason)
            .HasColumnName("last_retry_reason")
            .HasMaxLength(500);

        builder.Property(d => d.LastRetriedAt)
            .HasColumnName("last_retried_at");

        builder.HasIndex(d => d.CompanyId);
        builder.HasIndex(d => new { d.Status, d.RequestedAt });

        // At most one durable disablement record per application user — repeated delivery of the
        // same (or a reconciliation-republished) EmployeeDepartureFinalisedIntegrationEvent must
        // never enqueue a second disablement attempt for the same account.
        builder.HasIndex(d => d.ApplicationUserId)
            .IsUnique()
            .HasDatabaseName("ix_account_disablements_application_user_id");
    }
}
