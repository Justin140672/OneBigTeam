using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Features.RetryInterviewOutcomeReconciliation;

internal sealed class RetryInterviewOutcomeReconciliationHandler(
    RecruitmentDbContext db,
    ITaskCompletionRecovery taskRecovery,
    InterviewOutcomeTaskReconciliationService reconciliationService,
    IAuditEventPublisher auditPublisher,
    IClock clock,
    ILogger<RetryInterviewOutcomeReconciliationHandler> logger)
{
    public const string StatusCompleted = "completed";
    public const string StatusOutstanding = "outstanding";
    public const string StatusBlocked = "blocked";

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
                case TaskCompletionResetOutcome.Conflict:
                    return Result.Failure<RetryInterviewOutcomeReconciliationResponse>(Error.Concurrency(
                        "The Tasks completion operation was changed by another request. Reload and try again."));
            }

            tasksReset = reset.Outcome == TaskCompletionResetOutcome.Reset;
        }

        var now = clock.UtcNowOffset();
        record.Unblock(operatorUserId, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(record).State = EntityState.Detached;
            var current = await db.InterviewOutcomeTaskReconciliations.AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == request.ReconciliationId && r.CompanyId == request.CompanyId, cancellationToken);

            return current is { IsBlocked: false }
                ? Result.Success(ToResponse(current, wasBlocked: true, tasksReset))
                : Result.Failure<RetryInterviewOutcomeReconciliationResponse>(
                    Error.Concurrency("This reconciliation was changed by another request. Reload and try again."));
        }

        logger.LogWarning(
            "Blocked interview outcome reconciliation unblocked by operator. ReconciliationId={ReconciliationId} CompanyId={CompanyId} InterviewId={InterviewId} ApplicationId={ApplicationId} TasksOperationId={TasksOperationId} OperatorUserId={OperatorUserId} FailureCategory={FailureCategory} TasksCompletionReset={TasksCompletionReset}",
            record.Id, record.CompanyId, record.InterviewId, record.ApplicationId, tasksOperationId, operatorUserId, category, tasksReset);

        await auditPublisher.PublishAsync(
            new InterviewOutcomeReconciliationRepairedAuditEvent(
                record.CompanyId, record.Id, record.InterviewId, record.ApplicationId, operatorUserId,
                category, tasksOperationId, reason, now),
            cancellationToken);

        await reconciliationService.RunAsync(record, cancellationToken);

        return Result.Success(ToResponse(record, wasBlocked: true, tasksReset));
    }

    private static RetryInterviewOutcomeReconciliationResponse ToResponse(
        InterviewOutcomeTaskReconciliation record, bool wasBlocked, bool tasksReset) =>
        new(record.Id, record.InterviewId,
            record.IsBlocked ? StatusBlocked : record.CompletedAt is not null ? StatusCompleted : StatusOutstanding,
            wasBlocked, tasksReset);
}
