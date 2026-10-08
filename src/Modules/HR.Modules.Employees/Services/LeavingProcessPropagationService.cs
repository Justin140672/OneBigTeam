using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Services;

internal sealed class LeavingProcessPropagationService(
    EmployeesDbContext dbContext,
    IIntegrationEventPublisher integrationEventPublisher,
    IClock clock,
    ILogger<LeavingProcessPropagationService> logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    public const int DefaultBatchSize = 100;

    public LeavingProcessPropagation Stage(
        EmployeeLeavingProcess process, string operationType, DateTimeOffset now)
    {
        var ambient = executionContextAccessor?.Current;
        var isCancellation = operationType is LeavingProcessPropagation.OperationCancelled
            or LeavingProcessPropagation.OperationReconciledCancelled;

        var propagation = LeavingProcessPropagation.Create(
            process.CompanyId,
            process.EmployeeId,
            process.Id,
            operationType,
            isCancellation ? null : process.LeavingDate,
            isCancellation ? null : process.LastWorkingDay,
            now,
            now,
            ambient is null ? null : CorrelationIdGuid.Derive(ambient.CorrelationId),
            ambient?.MessageId);

        dbContext.LeavingProcessPropagations.Add(propagation);
        return propagation;
    }

    public async Task TryDispatchForEmployeeAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        try
        {
            var now = clock.UtcNowOffset();
            var due = await dbContext.LeavingProcessPropagations
                .Where(p => p.CompanyId == companyId
                    && p.EmployeeId == employeeId
                    && p.Status == LeavingProcessPropagation.StatusPending
                    && p.NextAttemptAt <= now)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync(cancellationToken);

            await DispatchAsync(due, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Detach();
            logger.LogError(
                ex,
                "Leaving-process propagation dispatch failed for employee {EmployeeId} (company {CompanyId}); the durable record remains pending and will be retried.",
                employeeId, companyId);
        }
    }

    public async Task<int> DispatchDueAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var due = await dbContext.LeavingProcessPropagations
            .Where(p => p.Status == LeavingProcessPropagation.StatusPending && p.NextAttemptAt <= now)
            .OrderBy(p => p.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return await DispatchAsync(due, cancellationToken);
    }

    public async Task<int> EnqueueMissingAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var legacy = await dbContext.EmployeeLeavingProcesses
            .Where(p => (p.Status == LeavingProcessStatus.InProgress || p.Status == LeavingProcessStatus.Cancelled)
                && !dbContext.LeavingProcessPropagations.Any(x => x.LeavingProcessId == p.Id)
                && !dbContext.EmployeeLeavingProcesses.Any(o =>
                    o.CompanyId == p.CompanyId && o.EmployeeId == p.EmployeeId && o.StartedAt > p.StartedAt))
            .OrderBy(p => p.StartedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var process in legacy)
        {
            Stage(
                process,
                process.Status == LeavingProcessStatus.InProgress
                    ? LeavingProcessPropagation.OperationReconciledStarted
                    : LeavingProcessPropagation.OperationReconciledCancelled,
                now);
        }

        if (legacy.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return legacy.Count;
    }

    public async Task<int> ResetStaleFailuresAsync(TimeSpan olderThan, int batchSize, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var cutoff = now - olderThan;

        var failed = await dbContext.LeavingProcessPropagations
            .Where(p => p.Status == LeavingProcessPropagation.StatusFailed && p.LastAttemptAt <= cutoff)
            .OrderBy(p => p.LastAttemptAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var propagation in failed)
            propagation.ResetForRetry(now);

        if (failed.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return failed.Count;
    }

    private async Task<int> DispatchAsync(
        IReadOnlyList<LeavingProcessPropagation> due, CancellationToken cancellationToken)
    {
        var processed = 0;
        var blockedEmployees = new HashSet<Guid>();

        foreach (var propagation in due)
        {
            if (blockedEmployees.Contains(propagation.EmployeeId))
                continue;

            var hasEarlierPending = await dbContext.LeavingProcessPropagations
                .AnyAsync(
                    p => p.CompanyId == propagation.CompanyId
                        && p.EmployeeId == propagation.EmployeeId
                        && p.Id != propagation.Id
                        && p.Status == LeavingProcessPropagation.StatusPending
                        && p.CreatedAt < propagation.CreatedAt,
                    cancellationToken);

            if (hasEarlierPending)
            {
                blockedEmployees.Add(propagation.EmployeeId);
                continue;
            }

            var delivered = await DeliverAsync(propagation, cancellationToken);
            processed++;

            if (!delivered)
                blockedEmployees.Add(propagation.EmployeeId);
        }

        return processed;
    }

    private async Task<bool> DeliverAsync(LeavingProcessPropagation propagation, CancellationToken cancellationToken)
    {
        var restoredContext = ExecutionContextInfo.Restore(
            (propagation.CorrelationId ?? propagation.Id).ToString("D"),
            propagation.Id,
            propagation.CausationId,
            ExecutionOrigin.ReconciliationJob);

        string? error = null;
        var confirmed = false;

        try
        {
            confirmed = propagation.IsCancellation
                ? await integrationEventPublisher.PublishAndConfirmAsync(
                    new EmployeeLeavingProcessCancelledIntegrationEvent(
                        propagation.CompanyId, propagation.EmployeeId, propagation.OccurredAt),
                    restoredContext,
                    cancellationToken)
                : await integrationEventPublisher.PublishAndConfirmAsync(
                    new EmployeeLeavingDateSetIntegrationEvent(
                        propagation.CompanyId, propagation.EmployeeId,
                        propagation.LeavingDate!.Value, propagation.LastWorkingDay!.Value, propagation.OccurredAt),
                    restoredContext,
                    cancellationToken);

            if (!confirmed)
                error = "One or more required consumers failed to apply the event; see logs for this correlation id.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogError(
                ex,
                "Leaving-process propagation {PropagationId} ({OperationType}) failed for employee {EmployeeId}. CorrelationId={CorrelationId} CausationId={CausationId}",
                propagation.Id, propagation.OperationType, propagation.EmployeeId,
                restoredContext.CorrelationId, propagation.CausationId);
        }

        var now = clock.UtcNowOffset();

        if (confirmed)
        {
            propagation.MarkProcessed(now);
        }
        else
        {
            propagation.MarkAttemptFailed(error ?? "Unknown failure.", now);

            logger.LogWarning(
                "Leaving-process propagation {PropagationId} ({OperationType}) for employee {EmployeeId} not applied: attempt {Attempt}, status {Status}, next attempt {NextAttemptAt}. CorrelationId={CorrelationId}",
                propagation.Id, propagation.OperationType, propagation.EmployeeId,
                propagation.AttemptCount, propagation.Status, propagation.NextAttemptAt, restoredContext.CorrelationId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return confirmed;
    }

    private void Detach()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            entry.State = EntityState.Detached;
    }
}
