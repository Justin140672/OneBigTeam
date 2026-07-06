using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

// Depends on InterviewOutcomeRecorder (not RecordInterviewOutcomeHandler) deliberately:
// RecordInterviewOutcomeHandler depends on ITaskCompleter, whose implementation graph
// (TaskCompleter -> TaskCompletionDispatcher -> InterviewFeedbackTaskCompletionAction ->
// IInterviewFeedbackService) would loop back here and create a DI constructor cycle.
// InterviewOutcomeRecorder contains the same recording logic without that dependency, and
// this service never needs the completion side-effect anyway: it is only invoked from
// InterviewFeedbackTaskCompletionAction, which runs after TaskCompleter has already marked
// the originating task Completed.
internal sealed class InterviewFeedbackService(
    RecruitmentDbContext db,
    InterviewOutcomeRecorder recorder) : IInterviewFeedbackService
{
    public async Task<Result> RecordFeedbackAsync(
        Guid companyId,
        Guid interviewId,
        Guid recordedByEmployeeId,
        string outcome,
        string? notes,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<InterviewOutcome>(outcome, ignoreCase: true, out var parsedOutcome))
            return Result.Failure(Error.Validation($"'{outcome}' is not a recognised interview outcome."));

        var location = await (
            from i in db.Interviews.AsNoTracking()
            join a in db.Applications.AsNoTracking() on i.ApplicationId equals a.Id
            where i.Id == interviewId && i.CompanyId == companyId
            select new { a.VacancyId, ApplicationId = a.Id })
            .SingleOrDefaultAsync(cancellationToken);

        if (location is null)
            return Result.Failure(Error.NotFound($"Interview '{interviewId}' was not found."));

        var result = await recorder.RecordAsync(
            new RecordInterviewOutcomeRequest
            {
                CompanyId     = companyId,
                VacancyId     = location.VacancyId,
                ApplicationId = location.ApplicationId,
                InterviewId   = interviewId,
                Outcome       = parsedOutcome,
                Notes         = notes,
            },
            recordedByEmployeeId,
            cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}
