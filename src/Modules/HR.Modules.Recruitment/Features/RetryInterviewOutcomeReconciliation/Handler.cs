using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.RetryInterviewOutcomeReconciliation;

internal sealed class RetryInterviewOutcomeReconciliationHandler(
    RecruitmentDbContext db,
    ITaskCompletionRecovery taskRecovery,
    InterviewOutcomeTaskReconciliationService reconciliationService,
    InterviewOutcomeRepairService repairService)
{
    public const string StatusCompleted = "completed";
    public const string StatusOutstanding = "outstanding";
    public const string StatusBlocked = "blocked";
    public const string StatusCompletedWaived = "completed_waived";

    public async Task<Result<RetryInterviewOutcomeReconciliationResponse>> HandleAsync(
        RetryInterviewOutcomeReconciliationRequest request,
        Guid operatorUserId,
        CancellationToken cancellationToken)
    {
        var record = await db.InterviewOutcomeTaskReconciliations
            .SingleOrDefaultAsync(r => r.Id == request.ReconciliationId && r.CompanyId == request.CompanyId, cancellationToken);

        if (record is null)
            return Result.Failure<RetryInterviewOutcomeReconciliationResponse>(
                Error.NotFound($"Interview outcome reconciliation '{request.ReconciliationId}' was not found."));

        if (!record.IsBlocked)
            return Result.Success(ToResponse(record, wasBlocked: false, tasksReset: false));

        var category = record.BlockedCategory ?? string.Empty;
        var tasksOperationId = record.BlockedTasksOperationId;
        var reason = request.Reason.Trim();
        var tasksReset = false;

        if (category == InterviewOutcomeTaskReconciliation.BlockedTaskTerminalFailure && tasksOperationId is { } operationId)
        {
            var reset = await taskRecovery.ResetTerminalCompletionAsync(
                record.CompanyId, operationId, operatorUserId, reason, cancellationToken);

            switch (reset.Outcome)
            {
                case TaskCompletionResetOutcome.NotFound:
                    return Result.Failure<RetryInterviewOutcomeReconciliationResponse>(Error.Conflict(
                        $"Tasks completion operation '{operationId}' no longer exists; the blocked reconciliation needs manual investigation."));
                case TaskCompletionResetOutcome.DataIntegrityFailure:
                    return Result.Failure<RetryInterviewOutcomeReconciliationResponse>(Error.Conflict(
                        $"Tasks completion operation '{operationId}' is a data-integrity failure; the blocked reconciliation needs manual investigation."));
                case TaskCompletionResetOutcome.Conflict:
                    return Result.Failure<RetryInterviewOutcomeReconciliationResponse>(Error.Concurrency(
                        "The Tasks completion operation was changed by another request. Reload and try again."));
            }

            tasksReset = reset.Outcome == TaskCompletionResetOutcome.Reset;
        }

        var action = await repairService.UnblockAsync(
            record, operatorUserId, reason, InterviewOutcomeRepairAction.SourceOperator, cancellationToken);

        if (action is null)
        {
            var current = await db.InterviewOutcomeTaskReconciliations.AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == request.ReconciliationId && r.CompanyId == request.CompanyId, cancellationToken);

            return current is { IsBlocked: false }
                ? Result.Success(ToResponse(current, wasBlocked: true, tasksReset))
                : Result.Failure<RetryInterviewOutcomeReconciliationResponse>(
                    Error.Concurrency("This reconciliation was changed by another request. Reload and try again."));
        }

        await reconciliationService.RunAsync(record, cancellationToken);

        return Result.Success(ToResponse(record, wasBlocked: true, tasksReset, action));
    }

    private static RetryInterviewOutcomeReconciliationResponse ToResponse(
        InterviewOutcomeTaskReconciliation record, bool wasBlocked, bool tasksReset,
        InterviewOutcomeRepairAction? action = null) =>
        new(record.Id, record.InterviewId,
            record.IsBlocked ? StatusBlocked : record.IsWaived ? StatusCompletedWaived : record.CompletedAt is not null ? StatusCompleted : StatusOutstanding,
            wasBlocked, tasksReset, action?.Id, record.RepairCount);
}
