using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.MoveApplicationStage;

/// <summary>
/// Generic Kanban drag-and-drop move: unlike the named transition endpoints (Offer/Hire/Reject/etc.),
/// the caller here only knows the target RecruitmentStage id. Ticket #99: since stages are now fully
/// data-driven (no compiled ApplicationStatusTransitions graph any more), validity here is simply
/// "the target stage exists, belongs to this company, and is active" plus "the application isn't
/// withdrawn or already on a terminal stage" — a company may freely reorder/insert stages, so no
/// stricter linear-order transition check is enforced. Records stage history (#66) and publishes the
/// integration + audit events (#65/#67) exactly once, the same way the named-transition handlers do
/// via RecruitmentStageChangeRecorder.
/// </summary>
internal sealed class MoveApplicationStageHandler(
    RecruitmentDbContext db,
    IClock clock,
    RecruitmentStageChangeRecorder recorder)
{
    public async Task<Result<MoveApplicationStageResponse>> HandleAsync(
        MoveApplicationStageRequest request,
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
            return Result.Failure<MoveApplicationStageResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<MoveApplicationStageResponse>(
                Error.Validation("Cannot move a withdrawn application to a different stage."));

        var currentStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (currentStage is { IsTerminal: true })
            return Result.Failure<MoveApplicationStageResponse>(
                Error.Validation($"Cannot move an application off the terminal stage '{currentStage.Name}'."));

        var newStage = await db.RecruitmentStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == request.NewStageId && s.CompanyId == request.CompanyId, cancellationToken);

        if (newStage is null)
            return Result.Failure<MoveApplicationStageResponse>(
                Error.NotFound($"Recruitment stage '{request.NewStageId}' was not found."));

        if (!newStage.IsActive)
            return Result.Failure<MoveApplicationStageResponse>(
                Error.Validation($"Cannot move an application to the inactive stage '{newStage.Name}'."));

        var previousStageId = application.CurrentStageId;
        var now = clock.UtcNowOffset();

        application.MoveToStage(newStage.Id, now);

        recorder.AddHistoryEntry(application, previousStageId, performedBy, now, request.Notes);
        await db.SaveChangesAsync(cancellationToken);

        await recorder.PublishStageChangedEventsAsync(application, previousStageId, performedBy, now, cancellationToken);

        return Result.Success(new MoveApplicationStageResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.CurrentStageId,
            application.InterviewOutcome,
            application.Notes,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt));
    }
}
