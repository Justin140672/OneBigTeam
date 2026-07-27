using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.MoveApplicationStage;

/// <summary>
/// Generic Kanban drag-and-drop move: unlike the named transition endpoints (Offer/Hire/Reject/etc.),
/// the caller here only knows the target column, so validity is checked against
/// ApplicationStatusTransitions via Application.MoveToStage (ticket #64) rather than a dedicated named
/// method. Records stage history (#66) and publishes the integration + audit events (#65/#67) exactly
/// once, the same way the named-transition handlers do via RecruitmentStageChangeRecorder.
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

        var previousStatus = application.Status;
        var now = clock.UtcNowOffset();

        try
        {
            application.MoveToStage(request.NewStatus, now);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<MoveApplicationStageResponse>(Error.Validation(ex.Message));
        }

        recorder.AddHistoryEntry(application, previousStatus, performedBy, now, request.Notes);
        await db.SaveChangesAsync(cancellationToken);

        await recorder.PublishStageChangedEventsAsync(application, previousStatus, performedBy, now, cancellationToken);

        return Result.Success(new MoveApplicationStageResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.Status,
            application.InterviewOutcome,
            application.Notes,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt));
    }
}
