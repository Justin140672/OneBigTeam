using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class InternalOfferTaskEffectConfiguration : IEntityTypeConfiguration<InternalOfferTaskEffect>
{
    public void Configure(EntityTypeBuilder<InternalOfferTaskEffect> builder)
    {
        builder.ToTable("internal_offer_task_effects");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(e => e.ApplicationId).HasColumnName("application_id").IsRequired();
        builder.Property(e => e.OfferVersion).HasColumnName("offer_version").IsRequired();
        builder.Property(e => e.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(e => e.MadeByUserId).HasColumnName("made_by_user_id").IsRequired();
        builder.Property(e => e.JobTitle).HasColumnName("job_title").HasMaxLength(500).IsRequired();
        builder.Property(e => e.ResponseDeadline).HasColumnName("response_deadline");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.TaskCreatedAt).HasColumnName("task_created_at");
        builder.Property(e => e.ClosedAt).HasColumnName("closed_at");
        builder.Property(e => e.ResponseNotifiedAt).HasColumnName("response_notified_at");
        builder.Property(e => e.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(e => e.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(e => e.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);

        builder.HasIndex(e => new { e.ApplicationId, e.OfferVersion }).IsUnique();
        builder.HasIndex(e => new { e.CompanyId, e.ApplicationId });
        builder.HasIndex(e => e.CreatedAt)
            .HasFilter("closed_at IS NULL")
            .HasDatabaseName("ix_internal_offer_task_effects_open");
    }
}
