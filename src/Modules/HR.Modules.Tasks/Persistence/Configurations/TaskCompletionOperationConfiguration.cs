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
