using HR.Modules.Tasks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Tasks.Persistence.Configurations;

internal sealed class TaskRecoveryActionConfiguration : IEntityTypeConfiguration<TaskRecoveryAction>
{
    public void Configure(EntityTypeBuilder<TaskRecoveryAction> builder)
    {
        builder.ToTable("task_recovery_actions");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(a => a.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(a => a.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(a => a.OperationId).HasColumnName("operation_id").IsRequired();
        builder.Property(a => a.ActionType).HasColumnName("action_type").HasMaxLength(32).HasDefaultValue(TaskRecoveryAction.ActionReset).IsRequired();
        builder.Property(a => a.ResolutionType).HasColumnName("resolution_type").HasMaxLength(32);
        builder.Property(a => a.PreviousStatus).HasColumnName("previous_status").HasMaxLength(32);
        builder.Property(a => a.ResultingStatus).HasColumnName("resulting_status").HasMaxLength(32);
        builder.Property(a => a.EvidenceSupplied).HasColumnName("evidence_supplied").HasDefaultValue(false);
        builder.Property(a => a.OperationKind).HasColumnName("operation_kind").HasMaxLength(32).IsRequired();
        builder.Property(a => a.OperatorUserId).HasColumnName("operator_user_id").IsRequired();
        builder.Property(a => a.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
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

        builder.HasIndex(a => new { a.OperationId, a.ActionType, a.SequenceNumber })
            .IsUnique()
            .HasDatabaseName("ix_task_recovery_actions_operation_sequence");
        builder.HasIndex(a => a.OccurredAt)
            .HasFilter("audit_delivered_at IS NULL")
            .HasDatabaseName("ix_task_recovery_actions_undelivered");
        builder.HasIndex(a => new { a.CompanyId, a.TaskId });
    }
}
