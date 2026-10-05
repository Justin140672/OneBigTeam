using HR.Modules.Tasks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Tasks.Persistence.Configurations;

internal sealed class TaskCompletionOperationConfiguration : IEntityTypeConfiguration<TaskCompletionOperation>
{
    public void Configure(EntityTypeBuilder<TaskCompletionOperation> builder)
    {
        builder.ToTable("task_completion_operations");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(o => o.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(o => o.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(o => o.CompletedBy).HasColumnName("completed_by").IsRequired();
        builder.Property(o => o.OutcomeDecision).HasColumnName("outcome_decision").HasMaxLength(200);
        builder.Property(o => o.OutcomeReason).HasColumnName("outcome_reason").HasMaxLength(2000);
        builder.Property(o => o.CommandFingerprint).HasColumnName("command_fingerprint").HasMaxLength(80);
        builder.Property(o => o.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(o => o.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(o => o.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(o => o.FailureReason).HasColumnName("failure_reason").HasMaxLength(2000);
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.ProcessedAt).HasColumnName("processed_at");

        // Ticket 19 (P2): claim/lease columns.
        builder.Property(o => o.Version)
            .HasColumnName("version")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(o => o.ClaimedBy).HasColumnName("claimed_by");
        builder.Property(o => o.LeaseExpiresAt).HasColumnName("lease_expires_at");

        // Ticket 23 (P2): nullable so existing rows remain usable without a backfill.
        builder.Property(o => o.CorrelationId).HasColumnName("correlation_id");
        builder.Property(o => o.CausationId).HasColumnName("causation_id");
        builder.Property(o => o.MessageId).HasColumnName("message_id");

        builder.Property(o => o.TerminalFailureAt).HasColumnName("terminal_failure_at");
        builder.Property(o => o.FailureCategory).HasColumnName("failure_category").HasMaxLength(64);
        builder.Property(o => o.ResetCount).HasColumnName("reset_count").HasDefaultValue(0);
        builder.Property(o => o.LastResetAt).HasColumnName("last_reset_at");
        builder.Property(o => o.LastResetBy).HasColumnName("last_reset_by");
        builder.Property(o => o.SnapshotCapturedAt).HasColumnName("snapshot_captured_at");
        builder.Property(o => o.NotificationRequired).HasColumnName("notification_required").HasDefaultValue(false);
        builder.Property(o => o.SnapshotAssignedEmployeeId).HasColumnName("snapshot_assigned_employee_id");
        builder.Property(o => o.SnapshotTaskTitle).HasColumnName("snapshot_task_title").HasMaxLength(200);
        builder.Property(o => o.SnapshotTaskDescription).HasColumnName("snapshot_task_description").HasMaxLength(500);
        builder.Property(o => o.PreviousTaskStatus).HasColumnName("previous_task_status").HasMaxLength(32);
        builder.Property(o => o.TaskCompletedAt).HasColumnName("task_completed_at");
        builder.Property(o => o.AdjudicationCount).HasColumnName("adjudication_count").HasDefaultValue(0);
        builder.Property(o => o.ResolutionType).HasColumnName("resolution_type").HasMaxLength(32);
        builder.Property(o => o.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(o => o.LastAdjudicatedAt).HasColumnName("last_adjudicated_at");
        builder.Property(o => o.LastAdjudicatedBy).HasColumnName("last_adjudicated_by");
        builder.Ignore(o => o.IsOperatorResolved);
        builder.Ignore(o => o.HasCompletionSnapshot);
        builder.Ignore(o => o.IsTerminalFailure);

        builder.HasIndex(o => o.TaskId);
        builder.HasIndex(o => new { o.CompanyId, o.Status });

        // Ticket 11 (P1): enforces "one active completion operation per task unless the prior
        // operation was rejected" at the database level, so two concurrent CompleteTask requests for
        // the same task can never both insert a Pending/DispatchApplied/Processed operation — the
        // loser gets a unique-violation and must re-read and converge on the winner's row (see
        // CompleteTaskHandler). A task whose only prior operation was Rejected is deliberately left
        // free to get a brand new one (excluded from the filter), since the underlying business
        // action never applied for that attempt.
        builder.HasIndex(o => o.TaskId)
            .IsUnique()
            .HasDatabaseName("ix_task_completion_operations_task_id_active")
            .HasFilter("status <> 'rejected'");
    }
}
