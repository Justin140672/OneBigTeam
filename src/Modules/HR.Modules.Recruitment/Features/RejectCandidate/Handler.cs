using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.RejectCandidate;

internal sealed class RejectCandidateHandler(RecruitmentDbContext db, IClock clock, RecruitmentStageChangeRecorder recorder)
{
    public async Task<Result<RejectCandidateResponse>> HandleAsync(
        RejectCandidateRequest request,
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
            return Result.Failure<RejectCandidateResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<RejectCandidateResponse>(
                Error.Validation("Cannot reject an application that has been withdrawn."));

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (currentStage is null)
            return Result.Failure<RejectCandidateResponse>(
                Error.NotFound($"Recruitment stage '{application.CurrentStageId}' was not found."));

        if (currentStage.IsTerminal)
            return Result.Failure<RejectCandidateResponse>(
                Error.Validation($"Cannot reject an application already on the terminal stage '{currentStage.Name}'."));

        var rejectedStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.CompanyId == request.CompanyId && s.IsActive && s.TerminalOutcome == RecruitmentStageTerminalOutcome.Rejected,
                cancellationToken);

        if (rejectedStage is null)
            return Result.Failure<RejectCandidateResponse>(
                Error.Validation("This company has no active 'Rejected' terminal recruitment stage configured."));

        var now = clock.UtcNowOffset();
        var previousStageId = application.CurrentStageId;

        application.RecordRejection(rejectedStage.Id, request.RejectionReason, now);
        recorder.AddHistoryEntry(application, previousStageId, performedBy, now, request.RejectionReason);
        await db.SaveChangesAsync(cancellationToken);
        await recorder.PublishStageChangedEventsAsync(application, previousStageId, performedBy, now, cancellationToken);

        return Result.Success(new RejectCandidateResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.CurrentStageId,
            application.InterviewOutcome,
            application.Notes,
            application.RejectionReason,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt));
    }
}
