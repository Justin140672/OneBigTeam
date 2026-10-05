using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class InterviewOutcomeTaskReconciliationConfiguration : IEntityTypeConfiguration<InterviewOutcomeTaskReconciliation>
{
    public void Configure(EntityTypeBuilder<InterviewOutcomeTaskReconciliation> builder)
    {
        builder.ToTable("interview_outcome_task_reconciliations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(r => r.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(r => r.ApplicationId).HasColumnName("application_id").IsRequired();
        builder.Property(r => r.InterviewId).HasColumnName("interview_id").IsRequired();
        builder.Property(r => r.RecordedBy).HasColumnName("recorded_by").IsRequired();
        builder.Property(r => r.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(r => r.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(r => r.CompletedAt).HasColumnName("completed_at");
        builder.Property(r => r.ClaimedUntil).HasColumnName("claimed_until");
        builder.Property(r => r.AuditDeliveredAt).HasColumnName("audit_delivered_at");
        builder.Property(r => r.Version).HasColumnName("version").IsConcurrencyToken().ValueGeneratedNever();
        builder.Property(r => r.BlockedAt).HasColumnName("blocked_at");
        builder.Property(r => r.BlockedCategory).HasColumnName("blocked_category").HasMaxLength(64);
        builder.Property(r => r.BlockedTaskId).HasColumnName("blocked_task_id");
        builder.Property(r => r.BlockedTasksOperationId).HasColumnName("blocked_tasks_operation_id");
        builder.Property(r => r.RepairCount).HasColumnName("repair_count").HasDefaultValue(0);
        builder.Property(r => r.LastRepairedAt).HasColumnName("last_repaired_at");
        builder.Property(r => r.LastRepairedBy).HasColumnName("last_repaired_by");
        builder.Ignore(r => r.IsBlocked);

        builder.HasIndex(r => r.InterviewId).IsUnique();
        builder.HasIndex(r => new { r.CompanyId, r.ApplicationId });
        builder.HasIndex(r => r.CreatedAt)
            .HasFilter("completed_at IS NULL AND blocked_at IS NULL")
            .HasDatabaseName("ix_interview_outcome_task_reconciliations_outstanding");
        builder.HasIndex(r => r.BlockedAt)
            .HasFilter("blocked_at IS NOT NULL")
            .HasDatabaseName("ix_interview_outcome_task_reconciliations_blocked");
    }
}
