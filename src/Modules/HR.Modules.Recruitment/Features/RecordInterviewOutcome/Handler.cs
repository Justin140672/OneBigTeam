using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Recruitment.Features.RecordInterviewOutcome;

/// <summary>
/// Handles the direct "record interview outcome" API endpoint. Delegates the actual
/// recording work to <see cref="InterviewOutcomeRecorder"/> (shared with the generic
/// task-completion path via InterviewFeedbackService) and additionally completes the
/// associated feedback task, since on this direct-API path nothing else does so.
/// </summary>
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
