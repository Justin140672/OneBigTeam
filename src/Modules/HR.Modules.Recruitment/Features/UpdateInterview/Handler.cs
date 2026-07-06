using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.UpdateInterview;

internal sealed class UpdateInterviewHandler(RecruitmentDbContext db, IClock clock)
{
    public async Task<Result<UpdateInterviewResponse>> HandleAsync(
        UpdateInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var applicationExists = await db.Applications
            .AnyAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (!applicationExists)
            return Result.Failure<UpdateInterviewResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        var interview = await db.Interviews
            .SingleOrDefaultAsync(
                i => i.Id == request.InterviewId &&
                     i.ApplicationId == request.ApplicationId &&
                     i.CompanyId == request.CompanyId,
                cancellationToken);

        if (interview is null)
            return Result.Failure<UpdateInterviewResponse>(
                Error.NotFound($"Interview '{request.InterviewId}' was not found."));

        if (interview.Outcome != Domain.InterviewOutcome.Pending)
            return Result.Failure<UpdateInterviewResponse>(
                Error.Validation($"Cannot update an interview with outcome '{interview.Outcome}'."));

        var now = clock.UtcNowOffset();

        interview.UpdateDetails(
            request.InterviewerEmployeeId,
            request.ScheduledAt,
            request.DurationMinutes,
            request.Location,
            now);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateInterviewResponse(
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
