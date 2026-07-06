using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.RecordInterviewOutcome;

internal sealed class RecordInterviewOutcomeHandler(RecruitmentDbContext db, ITaskCompleter taskCompleter, IClock clock)
{
    public async Task<Result<RecordInterviewOutcomeResponse>> HandleAsync(
        RecordInterviewOutcomeRequest request,
        Guid recordedBy,
        CancellationToken cancellationToken)
    {
        var applicationExists = await db.Applications
            .AnyAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (!applicationExists)
            return Result.Failure<RecordInterviewOutcomeResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        var interview = await db.Interviews
            .SingleOrDefaultAsync(
                i => i.Id == request.InterviewId &&
                     i.ApplicationId == request.ApplicationId &&
                     i.CompanyId == request.CompanyId,
                cancellationToken);

        if (interview is null)
            return Result.Failure<RecordInterviewOutcomeResponse>(
                Error.NotFound($"Interview '{request.InterviewId}' was not found."));

        if (interview.Outcome != Domain.InterviewOutcome.Pending)
            return Result.Failure<RecordInterviewOutcomeResponse>(
                Error.Validation($"Cannot record an outcome for an interview with outcome '{interview.Outcome}'."));

        var now = clock.UtcNowOffset();

        interview.RecordOutcome(request.Outcome, request.Notes, now);
        await db.SaveChangesAsync(cancellationToken);

        await taskCompleter.CompleteBySourceEntityAsync(
            request.CompanyId,
            interview.Id,
            TaskSource.Recruitment,
            TaskActionType.Complete,
            recordedBy,
            cancellationToken);

        return Result.Success(new RecordInterviewOutcomeResponse(
            interview.Id,
            interview.CompanyId,
            interview.ApplicationId,
            interview.InterviewerEmployeeId,
            interview.ScheduledAt,
            interview.DurationMinutes,
            interview.Location,
            interview.Outcome,
            interview.Notes,
            interview.CreatedAt,
            interview.UpdatedAt));
    }
}
