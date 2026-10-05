using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Tasks.Services;

internal enum AdjudicationOutcome
{
    Applied,
    AlreadyApplied,
    NotFound,
    InvalidState,
    InvalidEvidence,
    VerificationFailed,
    Concurrency,
}

internal sealed record AdjudicationEvidence(
    bool NotificationRequired,
    Guid? AssignedEmployeeId,
    DateTimeOffset? CompletedAt,
    string? PreviousTaskStatus,
    string? TaskTitle);

internal sealed record AdjudicationResult(
    AdjudicationOutcome Outcome,
    Guid OperationId,
    Guid? TaskId = null,
    string? Status = null,
    Guid? RecoveryActionId = null,
    int AdjudicationCount = 0,
    string? ResolutionType = null);

internal sealed class TaskCompletionAdjudicator(
    TasksDbContext dbContext,
    IClock clock,
    TaskRecoveryAuditDelivery auditDelivery,
    INotificationWriter notificationWriter,
    IAuditEventExistenceReader auditExistenceReader,
    ILogger<TaskCompletionAdjudicator> logger,
    IExecutionContextAccessor? executionContextAccessor = null,
    IBackgroundJobClient? backgroundJobClient = null)
{
    public static readonly string[] AllowedPreviousStatuses = ["Open", "InProgress"];

    public static bool IsKnownResolution(string? resolution) =>
        resolution is TaskCompletionOperation.ResolutionEvidenceRetry
            or TaskCompletionOperation.ResolutionEffectsVerified
            or TaskCompletionOperation.ResolutionWaived;

    public async Task<AdjudicationResult> AdjudicateAsync(
        Guid companyId,
        Guid operationId,
        Guid operatorUserId,
        string resolution,
        string reason,
        AdjudicationEvidence? evidence,
        CancellationToken cancellationToken)
    {
        if (!IsKnownResolution(resolution) || string.IsNullOrWhiteSpace(reason))
            return new AdjudicationResult(AdjudicationOutcome.InvalidEvidence, operationId);

        var operation = await dbContext.TaskCompletionOperations
            .SingleOrDefaultAsync(o => o.Id == operationId && o.CompanyId == companyId, cancellationToken);

        if (operation is null)
            return new AdjudicationResult(AdjudicationOutcome.NotFound, operationId);

        if (operation.Status != TaskCompletionOperation.StatusDataIntegrityFailure)
        {
            var outcome = operation.AdjudicationCount > 0 && operation.ResolutionType == resolution
                ? AdjudicationOutcome.AlreadyApplied
                : AdjudicationOutcome.InvalidState;
            return Describe(operation, outcome, null);
        }

        var now = clock.UtcNowOffset();
        var previousStatus = operation.Status;
        var evidenceSupplied = false;

        switch (resolution)
        {
            case TaskCompletionOperation.ResolutionEvidenceRetry:
                if (!TryValidateEvidence(evidence, now, requireDetails: true))
                    return Describe(operation, AdjudicationOutcome.InvalidEvidence, null);

                operation.CaptureCompletionSnapshot(
                    evidence!.AssignedEmployeeId, evidence.TaskTitle ?? "Task", null,
                    evidence.PreviousTaskStatus!, evidence.CompletedAt!.Value, now);
                operation.BeginEvidenceRetry(operatorUserId, now);
                evidenceSupplied = true;
                break;

            case TaskCompletionOperation.ResolutionEffectsVerified:
                if (!await VerifyEffectsAsync(operation, evidence, now, cancellationToken))
                    return Describe(operation, AdjudicationOutcome.VerificationFailed, null);

                operation.MarkEffectsVerified(operatorUserId, now);
                break;

            default:
                operation.MarkWaived(operatorUserId, now);
                break;
        }

        var correlation = executionContextAccessor?.Current is { } context
            ? CorrelationIdGuid.Derive(context.CorrelationId)
            : (Guid?)null;

        var action = TaskRecoveryAction.CreateAdjudication(
            operation.CompanyId, operation.TaskId, operation.Id, operatorUserId,
            reason.Length > 500 ? reason[..500] : reason, operation.AdjudicationCount, resolution,
            previousStatus, operation.Status, evidenceSupplied, now, correlation);

        var expectedVersion = operation.Version;
        var savedOperationId = operation.Id;
        var resultingStatus = operation.Status;

        if (await dbContext.TaskRecoveryActions.AsNoTracking().AnyAsync(a => a.Id == action.Id, cancellationToken))
        {
            dbContext.Entry(operation).State = EntityState.Detached;
            return await ReloadLoserAsync(companyId, savedOperationId, resolution, cancellationToken);
        }

        dbContext.TaskRecoveryActions.Add(action);

        var saved = false;
        try
        {
            saved = (await dbContext.SaveChangesWithConcurrencyAsync(
                operation, expectedVersion, "This task completion operation was changed by another request.",
                cancellationToken)).IsSuccess;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(operation).State = EntityState.Detached;

            if (!await dbContext.TaskRecoveryActions.AsNoTracking().AnyAsync(a => a.Id == action.Id, CancellationToken.None))
                throw;
        }

        if (!saved)
        {
            dbContext.Entry(action).State = EntityState.Detached;
            dbContext.Entry(operation).State = EntityState.Detached;
            return await ReloadLoserAsync(companyId, savedOperationId, resolution, cancellationToken);
        }

        logger.LogWarning(
            "Task completion data-integrity failure adjudicated. CompanyId={CompanyId} TaskId={TaskId} TasksOperationId={TasksOperationId} RecoveryActionId={RecoveryActionId} OperatorUserId={OperatorUserId} ResolutionType={ResolutionType} ResultingStatus={ResultingStatus} AdjudicationCount={AdjudicationCount} CorrelationId={CorrelationId}",
            operation.CompanyId, operation.TaskId, operation.Id, action.Id, operatorUserId, resolution,
            resultingStatus, operation.AdjudicationCount, correlation);

        if (resolution == TaskCompletionOperation.ResolutionEvidenceRetry)
        {
            try
            {
                backgroundJobClient?.Enqueue<TaskCompletionEffectsJob>(
                    job => job.ProcessAsync(savedOperationId, companyId));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Could not enqueue the effects job after adjudication; the completion reconciliation sweep will pick it up. TasksOperationId={TasksOperationId}",
                    savedOperationId);
            }
        }

        try
        {
            await auditDelivery.DeliverAsync(action, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Immediate audit delivery for adjudication action {RecoveryActionId} failed; the durable intent remains outstanding. CompanyId={CompanyId} TasksOperationId={TasksOperationId}",
                action.Id, companyId, savedOperationId);
        }

        return Describe(operation, AdjudicationOutcome.Applied, action.Id);
    }

    private async Task<AdjudicationResult> ReloadLoserAsync(
        Guid companyId, Guid operationId, string resolution, CancellationToken cancellationToken)
    {
        var current = await dbContext.TaskCompletionOperations.AsNoTracking()
            .SingleAsync(o => o.Id == operationId && o.CompanyId == companyId, cancellationToken);

        var outcome = current.Status != TaskCompletionOperation.StatusDataIntegrityFailure
            && current.ResolutionType == resolution
            ? AdjudicationOutcome.AlreadyApplied
            : AdjudicationOutcome.Concurrency;

        return Describe(current, outcome, null);
    }

    private static AdjudicationResult Describe(TaskCompletionOperation operation, AdjudicationOutcome outcome, Guid? actionId) =>
        new(outcome, operation.Id, operation.TaskId, operation.Status, actionId, operation.AdjudicationCount, operation.ResolutionType);

    private static bool TryValidateEvidence(AdjudicationEvidence? evidence, DateTimeOffset now, bool requireDetails)
    {
        if (evidence is null)
            return false;

        if (evidence.NotificationRequired && evidence.AssignedEmployeeId is null)
            return false;

        if (!requireDetails)
            return true;

        return evidence.CompletedAt is { } completedAt && completedAt <= now.AddMinutes(1)
            && evidence.PreviousTaskStatus is not null
            && AllowedPreviousStatuses.Contains(evidence.PreviousTaskStatus)
            && (evidence.TaskTitle is null || evidence.TaskTitle.Length <= 200);
    }

    private async Task<bool> VerifyEffectsAsync(
        TaskCompletionOperation operation, AdjudicationEvidence? evidence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        bool notificationRequired;
        Guid? employeeId;

        if (operation.HasCompletionSnapshot)
        {
            notificationRequired = operation.NotificationRequired;
            employeeId = operation.SnapshotAssignedEmployeeId;
        }
        else
        {
            if (!TryValidateEvidence(evidence, now, requireDetails: false))
                return false;

            notificationRequired = evidence!.NotificationRequired;
            employeeId = evidence.AssignedEmployeeId;
        }

        if (!await auditExistenceReader.ExistsAsync(operation.TaskId, cancellationToken))
            return false;

        return !notificationRequired
            || (employeeId is { } employee
                && await notificationWriter.ExistsAsync(
                    employee, operation.TaskId, NotificationType.TaskCompleted, cancellationToken));
    }
}
