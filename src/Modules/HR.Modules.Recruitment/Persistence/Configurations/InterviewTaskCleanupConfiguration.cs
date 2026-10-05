using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class InterviewTaskCleanupConfiguration : IEntityTypeConfiguration<InterviewTaskCleanup>
{
    public void Configure(EntityTypeBuilder<InterviewTaskCleanup> builder)
    {
        builder.ToTable("interview_task_cleanups");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(c => c.ApplicationId).HasColumnName("application_id").IsRequired();
        builder.Property(c => c.InterviewIdsJson).HasColumnName("interview_ids_json").HasColumnType("text").IsRequired();
        builder.Property(c => c.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(c => c.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(c => c.CompletedAt).HasColumnName("completed_at");

        builder.Ignore(c => c.InterviewIds);

        builder.HasIndex(c => new { c.CompanyId, c.ApplicationId });
        builder.HasIndex(c => c.CreatedAt)
            .HasFilter("completed_at IS NULL")
            .HasDatabaseName("ix_interview_task_cleanups_outstanding");
    }
}
