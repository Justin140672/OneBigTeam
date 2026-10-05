using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class InterviewTaskEffectConfiguration : IEntityTypeConfiguration<InterviewTaskEffect>
{
    public void Configure(EntityTypeBuilder<InterviewTaskEffect> builder)
    {
        builder.ToTable("interview_task_effects");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(e => e.ApplicationId).HasColumnName("application_id").IsRequired();
        builder.Property(e => e.InterviewId).HasColumnName("interview_id").IsRequired();
        builder.Property(e => e.ScheduledBy).HasColumnName("scheduled_by").IsRequired();
        builder.Property(e => e.InterviewerEmployeeId).HasColumnName("interviewer_employee_id").IsRequired();
        builder.Property(e => e.ScheduledAt).HasColumnName("scheduled_at").IsRequired();
        builder.Property(e => e.CandidateName).HasColumnName("candidate_name").HasMaxLength(500).IsRequired();
        builder.Property(e => e.VacancyTitle).HasColumnName("vacancy_title").HasMaxLength(500).IsRequired();
        builder.Property(e => e.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(e => e.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(e => e.CompletedAt).HasColumnName("completed_at");

        builder.HasIndex(e => e.InterviewId).IsUnique();
        builder.HasIndex(e => new { e.CompanyId, e.ApplicationId });
        builder.HasIndex(e => e.CreatedAt)
            .HasFilter("completed_at IS NULL")
            .HasDatabaseName("ix_interview_task_effects_outstanding");
    }
}
