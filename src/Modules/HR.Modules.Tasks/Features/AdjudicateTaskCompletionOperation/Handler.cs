using HR.Modules.Tasks.Services;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Features.AdjudicateTaskCompletionOperation;

internal sealed class AdjudicateTaskCompletionOperationHandler(TaskCompletionAdjudicator adjudicator)
{
    public async Task<Result<AdjudicateTaskCompletionOperationResponse>> HandleAsync(
        AdjudicateTaskCompletionOperationRequest request,
        Guid operatorUserId,
        CancellationToken cancellationToken)
    {
        var evidence = request.Evidence is { } e
            ? new AdjudicationEvidence(e.NotificationRequired, e.AssignedEmployeeId, e.CompletedAt, e.PreviousTaskStatus, e.TaskTitle?.Trim())
            : null;

        var result = await adjudicator.AdjudicateAsync(
            request.CompanyId, request.OperationId, operatorUserId, request.Resolution, request.Reason.Trim(),
            evidence, cancellationToken);

        return result.Outcome switch
        {
            AdjudicationOutcome.NotFound => Result.Failure<AdjudicateTaskCompletionOperationResponse>(
                Error.NotFound($"Task completion operation '{request.OperationId}' was not found.")),
            AdjudicationOutcome.InvalidState => Result.Failure<AdjudicateTaskCompletionOperationResponse>(
                Error.Conflict($"Task completion operation '{request.OperationId}' is not awaiting data-integrity adjudication (status '{result.Status}').")),
            AdjudicationOutcome.Concurrency => Result.Failure<AdjudicateTaskCompletionOperationResponse>(
                Error.Concurrency("This task completion operation was changed by another request. Reload and try again.")),
            AdjudicationOutcome.InvalidEvidence => Result.Failure<AdjudicateTaskCompletionOperationResponse>(
                Error.Validation("The supplied resolution or evidence is not valid for this operation.")),
            AdjudicationOutcome.VerificationFailed => Result.Failure<AdjudicateTaskCompletionOperationResponse>(
                Error.Conflict("The completion effects could not be verified from the authoritative audit and notification records.")),
            _ => Result.Success(new AdjudicateTaskCompletionOperationResponse(
                result.OperationId, result.TaskId, result.Status, result.ResolutionType,
                result.Outcome == AdjudicationOutcome.Applied, result.RecoveryActionId, result.AdjudicationCount)),
        };
    }
}
