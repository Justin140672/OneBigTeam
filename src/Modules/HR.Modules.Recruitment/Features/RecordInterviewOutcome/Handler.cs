using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Recruitment.Features.RecordInterviewOutcome;

internal sealed class RecordInterviewOutcomeHandler(InterviewOutcomeRecorder recorder, ITaskCompleter taskCompleter)
{
    public async Task<Result<RecordInterviewOutcomeResponse>> HandleAsync(
        RecordInterviewOutcomeRequest request,
        Guid recordedBy,
        CancellationToken cancellationToken)
    {
        var result = await recorder.RecordAsync(request, recordedBy, cancellationToken);

        if (result.IsFailure)
            return result;

        await taskCompleter.CompleteBySourceEntityAsync(
            request.CompanyId,
            request.InterviewId,
            TaskSource.Recruitment,
            TaskActionType.Complete,
            recordedBy,
            cancellationToken);

        return result;
    }
}
