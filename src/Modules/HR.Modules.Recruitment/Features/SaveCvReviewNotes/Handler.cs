using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.SaveCvReviewNotes;

/// <summary>
/// Ticket 1 — "Save Notes": records the recruiter's CV review notes against the application without
/// changing the recruitment stage. Notes belong to the Application (this candidate considered for
/// this vacancy). Reuses the existing tenant + vacancy scoping every application handler applies.
/// </summary>
internal sealed class SaveCvReviewNotesHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<SaveCvReviewNotesResponse>> HandleAsync(
        SaveCvReviewNotesRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<SaveCvReviewNotesResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<SaveCvReviewNotesResponse>(
                Error.Validation("Cannot record a CV review against a withdrawn application."));

        var now = clock.UtcNowOffset();
        application.RecordCvReview(request.CvReviewNotes, performedBy, now);

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new ApplicationCvReviewNotesSavedAuditEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                application.CandidateId,
                NotesCleared: application.CvReviewNotes is null,
                performedBy,
                now),
            cancellationToken);

        return Result.Success(new SaveCvReviewNotesResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.CurrentStageId,
            application.CvReviewNotes,
            application.CvReviewedAt,
            application.CvReviewedByUserId,
            application.UpdatedAt));
    }
}
