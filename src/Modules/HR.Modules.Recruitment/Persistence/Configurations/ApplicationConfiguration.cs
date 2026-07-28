using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Modules.Recruitment.Persistence.Configurations;

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.ToTable("applications");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();

        builder.Property(a => a.VacancyId)
            .HasColumnName("vacancy_id")
            .IsRequired();

        builder.Property(a => a.CandidateId)
            .HasColumnName("candidate_id")
            .IsRequired();

        builder.Property(a => a.CurrentStageId)
            .HasColumnName("current_stage_id")
            .IsRequired();

        builder.Property(a => a.WithdrawnAt)
            .HasColumnName("withdrawn_at");

        builder.Property(a => a.InterviewOutcome)
            .HasColumnName("interview_outcome")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(a => a.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasMaxLength(2000);

        builder.Property(a => a.AppliedAt)
            .HasColumnName("applied_at")
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(a => a.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(a => a.SourceExternalRecruiterId)
            .HasColumnName("source_external_recruiter_id");

        builder.HasOne<Vacancy>()
            .WithMany()
            .HasForeignKey(a => a.VacancyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Candidate>()
            .WithMany()
            .HasForeignKey(a => a.CandidateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RecruitmentStage>()
            .WithMany()
            .HasForeignKey(a => a.CurrentStageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.CompanyId);
        builder.HasIndex(a => a.VacancyId);
        builder.HasIndex(a => a.CandidateId);
        builder.HasIndex(a => new { a.VacancyId, a.CandidateId }).IsUnique();
        builder.HasIndex(a => a.SourceExternalRecruiterId);
        builder.HasIndex(a => a.CurrentStageId);
    }
}
