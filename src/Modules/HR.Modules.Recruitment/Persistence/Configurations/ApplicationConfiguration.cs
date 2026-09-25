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

        builder.Property(a => a.OfferApprovedAt)
            .HasColumnName("offer_approved_at");

        builder.Property(a => a.OfferApprovedByUserId)
            .HasColumnName("offer_approved_by_user_id");

        // Ticket 2: offer terms recorded on the application.
        builder.Property(a => a.OfferedSalary)
            .HasColumnName("offered_salary")
            .HasPrecision(18, 2);

        builder.Property(a => a.OfferedSalaryFrequency)
            .HasColumnName("offered_salary_frequency")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.OfferedStartDate)
            .HasColumnName("offered_start_date");

        builder.Property(a => a.OfferDate)
            .HasColumnName("offer_date");

        builder.Property(a => a.OfferNotes)
            .HasColumnName("offer_notes")
            .HasMaxLength(2000);

        builder.Property(a => a.OfferResponseStatus)
            .HasColumnName("offer_response_status")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.OfferMadeAt)
            .HasColumnName("offer_made_at");

        builder.Property(a => a.OfferRespondedAt)
            .HasColumnName("offer_responded_at");

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

        builder.Property(a => a.CvReviewNotes)
            .HasColumnName("cv_review_notes")
            .HasMaxLength(4000);

        builder.Property(a => a.CvReviewedAt)
            .HasColumnName("cv_reviewed_at");

        builder.Property(a => a.CvReviewedByUserId)
            .HasColumnName("cv_reviewed_by_user_id");

        builder.Property(a => a.AppliedAt)
            .HasColumnName("applied_at")
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(a => a.Version)
            .HasColumnName("version")
            .IsRequired()
            .IsConcurrencyToken()
            .HasDefaultValue(1);

        builder.Property(a => a.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(a => a.SourceExternalRecruiterId)
            .HasColumnName("source_external_recruiter_id");

        builder.Property(a => a.CvDocumentId)
            .HasColumnName("cv_document_id");

        // Internal recruitment Ticket 1: the submitted CV. A composite FK onto the candidate_documents
        // alternate key (id, candidate_id, company_id) makes the database itself guarantee the
        // referenced document belongs to this application's candidate and company — not just the
        // handlers. PostgreSQL's default MATCH SIMPLE semantics skip the check when cv_document_id is
        // null, so historic applications with no captured CV are unaffected. RESTRICT stops a
        // referenced CV being deleted until the application's reference is changed or removed.
        builder.HasOne<CandidateDocument>()
            .WithMany()
            .HasForeignKey(a => new { a.CvDocumentId, a.CandidateId, a.CompanyId })
            .HasPrincipalKey(cd => new { cd.Id, cd.CandidateId, cd.CompanyId })
            .HasConstraintName("fk_applications_candidate_documents_cv_document")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

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

        // Supports the FK (restrict-delete checks and "which applications reference this CV?"
        // lookups in DeleteCandidateDocument). Partial: most historic rows have no CV reference.
        builder.HasIndex(a => new { a.CvDocumentId, a.CandidateId, a.CompanyId })
            .HasDatabaseName("ix_applications_cv_document_id")
            .HasFilter("cv_document_id IS NOT NULL");
    }
}
