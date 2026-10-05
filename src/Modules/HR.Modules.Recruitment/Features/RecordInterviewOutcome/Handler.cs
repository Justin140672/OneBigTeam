using HR.Modules.Recruitment.Services;
using HR.SharedKernel;

namespace HR.Modules.Recruitment.Features.RecordInterviewOutcome;

internal sealed class RecordInterviewOutcomeHandler(
    InterviewOutcomeRecorder recorder,
    InterviewOutcomeTaskReconciliationService reconciliationService)
{
    public async Task<Result<RecordInterviewOutcomeResponse>> HandleAsync(
        RecordInterviewOutcomeRequest request,
        Guid recordedBy,
        CancellationToken cancellationToken)
    {
        var result = await recorder.RecordAsync(request, recordedBy, cancellationToken);

        if (result.IsFailure)
            return result;

        await reconciliationService.RunOutstandingForInterviewAsync(
            request.CompanyId, request.InterviewId, cancellationToken);

        return result;
    }
}
