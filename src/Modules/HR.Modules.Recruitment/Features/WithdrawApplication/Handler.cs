using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.WithdrawApplication;

internal sealed class WithdrawApplicationHandler(RecruitmentDbContext db, IClock clock, RecruitmentStageChangeRecorder recorder)
{
    public async Task<Result<WithdrawApplicationResponse>> HandleAsync(
        WithdrawApplicationRequest request,
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
            return Result.Failure<WithdrawApplicationResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.Status is ApplicationStatus.Hired or ApplicationStatus.Rejected or ApplicationStatus.Withdrawn)
            return Result.Failure<WithdrawApplicationResponse>(
                Error.Validation($"Cannot withdraw an application with status '{application.Status}'."));

        var now = clock.UtcNowOffset();
        var previousStatus = application.Status;

        application.Withdraw(now);
        recorder.AddHistoryEntry(application, previousStatus, performedBy, now);
        await db.SaveChangesAsync(cancellationToken);
        await recorder.PublishStageChangedEventsAsync(application, previousStatus, performedBy, now, cancellationToken);

        return Result.Success(new WithdrawApplicationResponse(
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
