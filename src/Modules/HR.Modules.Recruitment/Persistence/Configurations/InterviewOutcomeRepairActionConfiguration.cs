using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class InterviewOutcomeRepairActionConfiguration : IEntityTypeConfiguration<InterviewOutcomeRepairAction>
{
    public void Configure(EntityTypeBuilder<InterviewOutcomeRepairAction> builder)
    {
        builder.ToTable("interview_outcome_repair_actions");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(a => a.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(a => a.ReconciliationId).HasColumnName("reconciliation_id").IsRequired();
        builder.Property(a => a.InterviewId).HasColumnName("interview_id").IsRequired();
        builder.Property(a => a.ApplicationId).HasColumnName("application_id").IsRequired();
        builder.Property(a => a.TasksOperationId).HasColumnName("tasks_operation_id");
        builder.Property(a => a.BlockedCategory).HasColumnName("blocked_category").HasMaxLength(64).IsRequired();
        builder.Property(a => a.OperatorUserId).HasColumnName("operator_user_id").IsRequired();
        builder.Property(a => a.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        builder.Property(a => a.Source).HasColumnName("source").HasMaxLength(32).IsRequired();
        builder.Property(a => a.SequenceNumber).HasColumnName("sequence_number").IsRequired();
        builder.Property(a => a.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(a => a.CorrelationId).HasColumnName("correlation_id");
        builder.Property(a => a.AuditDeliveredAt).HasColumnName("audit_delivered_at");
        builder.Property(a => a.AuditAttemptCount).HasColumnName("audit_attempt_count").IsRequired();
        builder.Property(a => a.LastAuditAttemptAt).HasColumnName("last_audit_attempt_at");
        builder.Property(a => a.LastAuditFailure).HasColumnName("last_audit_failure").HasMaxLength(500);
        builder.Property(a => a.ClaimedUntil).HasColumnName("claimed_until");
        builder.Property(a => a.Version).HasColumnName("version").IsConcurrencyToken().ValueGeneratedNever();
        builder.Ignore(a => a.IsDelivered);

        builder.HasIndex(a => new { a.ReconciliationId, a.SequenceNumber })
            .IsUnique()
            .HasDatabaseName("ix_interview_outcome_repair_actions_reconciliation_sequence");
        builder.HasIndex(a => a.OccurredAt)
            .HasFilter("audit_delivered_at IS NULL")
            .HasDatabaseName("ix_interview_outcome_repair_actions_undelivered");
        builder.HasIndex(a => new { a.CompanyId, a.InterviewId });
    }
}
