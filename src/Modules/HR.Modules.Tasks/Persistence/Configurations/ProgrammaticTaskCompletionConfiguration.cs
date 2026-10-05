using HR.Modules.Tasks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Tasks.Persistence.Configurations;

internal sealed class ProgrammaticTaskCompletionConfiguration : IEntityTypeConfiguration<ProgrammaticTaskCompletion>
{
    public void Configure(EntityTypeBuilder<ProgrammaticTaskCompletion> builder)
    {
        builder.ToTable("programmatic_task_completions");

        builder.HasKey(c => c.TaskId);
        builder.Property(c => c.TaskId).HasColumnName("task_id").ValueGeneratedNever();
        builder.Property(c => c.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(c => c.CompletedBy).HasColumnName("completed_by").IsRequired();
        builder.Property(c => c.PreviousStatus).HasColumnName("previous_status").HasMaxLength(32).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.NotificationsClearedAt).HasColumnName("notifications_cleared_at");
        builder.Property(c => c.CompletionNotificationAt).HasColumnName("completion_notification_at");
        builder.Property(c => c.AuditPublishedAt).HasColumnName("audit_published_at");
        builder.Property(c => c.DispatchedAt).HasColumnName("dispatched_at");
        builder.Property(c => c.ConfirmedAt).HasColumnName("confirmed_at");
        builder.Property(c => c.OperationId).HasColumnName("operation_id").HasDefaultValueSql("gen_random_uuid()").IsRequired();
        builder.Property(c => c.DispatchMode).HasColumnName("dispatch_mode").HasMaxLength(32).HasDefaultValue(ProgrammaticTaskCompletion.DispatchModeDispatch).IsRequired();
        builder.Property(c => c.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(c => c.ClaimedBy).HasColumnName("claimed_by");
        builder.Property(c => c.ClaimedUntil).HasColumnName("claimed_until");
        builder.Property(c => c.AttemptCount).HasColumnName("attempt_count");
        builder.Property(c => c.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(c => c.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(c => c.TerminalFailureAt).HasColumnName("terminal_failure_at");
        builder.Property(c => c.ResetCount).HasColumnName("reset_count").HasDefaultValue(0);
        builder.Property(c => c.LastResetAt).HasColumnName("last_reset_at");
        builder.Property(c => c.LastResetBy).HasColumnName("last_reset_by");

        builder.HasIndex(c => c.CreatedAt)
            .HasFilter("confirmed_at IS NULL")
            .HasDatabaseName("ix_programmatic_task_completions_unconfirmed");

        builder.HasIndex(c => c.OperationId)
            .IsUnique()
            .HasDatabaseName("ix_programmatic_task_completions_operation_id");
    }
}
