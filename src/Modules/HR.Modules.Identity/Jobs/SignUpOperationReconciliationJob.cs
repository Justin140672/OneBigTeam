using Hangfire;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Jobs;

/// <summary>
/// Reconciles signup operations: compensates operations abandoned partway through provisioning (or
/// partway through compensation) once their lease plus the takeover safety interval has passed, sweeps
/// compensated operations for resources a stale worker created late (company, owned Supabase account),
/// and purges terminal operations after their retention window. A failed operation is never purged
/// until a sweep has succeeded; past retention an unswept failure keeps only its cleanup correlation
/// data (company id, email, operation id) and has everything else redacted. Bounded batches; every operation is
/// fenced with a versioned, token-guarded lease before any external side effect.
/// </summary>
internal sealed class SignUpOperationReconciliationJob(
    IdentityDbContext db,
    SignUpOperationCompensator compensator,
    IClock clock,
    ILogger<SignUpOperationReconciliationJob> logger)
{
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan CompensationLease = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan LateResourceWindow =
        Features.SignUp.SignUpHandler.Lease + SignUpOperation.TakeoverSafetyInterval + SignUpOperation.ExternalCallTimeout;
    internal const int BatchSize = 25;
    internal const int PurgeBatchSize = 500;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();
        var cutoff = now - StaleAfter;
        var leaseCutoff = now - SignUpOperation.TakeoverSafetyInterval;

        var candidateIds = await db.SignUpOperations
            .Where(o => o.Status == SignUpOperation.StatusInProgress
                && (o.UpdatedAt < cutoff || o.Stage == SignUpOperation.StageCompensating)
                && (o.LeaseExpiresAt == null || o.LeaseExpiresAt < leaseCutoff))
            .OrderBy(o => o.UpdatedAt)
            .Take(BatchSize)
            .Select(o => o.Id)
            .ToListAsync();

        foreach (var operationId in candidateIds)
        {
            try
            {
                db.ChangeTracker.Clear();
                var operation = await db.SignUpOperations.SingleOrDefaultAsync(o => o.Id == operationId);
                if (operation is null || !operation.IsInProgress || operation.LeaseIsActive(now))
                {
                    continue;
                }

                var expectedVersion = operation.Version;
                operation.TakeLease(now, CompensationLease);
                var claim = await db.SaveChangesWithConcurrencyAsync(
                    operation, expectedVersion, "Signup operation was claimed by another worker.", CancellationToken.None);
                if (claim.IsFailure)
                {
                    continue;
                }

                await compensator.CompensateAsync(
                    operation,
                    "registration_abandoned",
                    "Registration could not be completed. Please try again.",
                    releaseKey: true,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SignUpOperationReconciliationJob failed to compensate operation {OperationId}; it will be retried.", operationId);
            }
        }

        if (candidateIds.Count > 0)
        {
            logger.LogInformation("SignUpOperationReconciliationJob processed {Count} abandoned signup operations.", candidateIds.Count);
        }

        await SweepLateResourcesAsync(now);
        await PurgeExpiredTerminalOperationsAsync(now);
    }

    private async Task SweepLateResourcesAsync(DateTimeOffset now)
    {
        var sweepCutoff = now - LateResourceWindow;
        db.ChangeTracker.Clear();

        var ids = await db.SignUpOperations
            .Where(o => o.Status == SignUpOperation.StatusFailed && o.SweptAt == null && o.CompletedAt < sweepCutoff)
            .OrderBy(o => o.CompletedAt)
            .Take(BatchSize)
            .Select(o => o.Id)
            .ToListAsync();

        foreach (var operationId in ids)
        {
            try
            {
                db.ChangeTracker.Clear();
                var operation = await db.SignUpOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId);
                if (operation is null || operation.Status != SignUpOperation.StatusFailed)
                {
                    continue;
                }

                await compensator.RemoveLateResourcesAsync(operation, CancellationToken.None);

                await db.SignUpOperations
                    .Where(o => o.Id == operationId)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.SweptAt, now));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SignUpOperationReconciliationJob failed to sweep late resources of operation {OperationId}; it will be retried.", operationId);
            }
        }
    }

    private async Task PurgeExpiredTerminalOperationsAsync(DateTimeOffset now)
    {
        var retentionCutoff = now - SignUpOperation.Retention;
        db.ChangeTracker.Clear();

        await db.SignUpOperations
            .Where(o => o.Status == SignUpOperation.StatusFailed && o.SweptAt == null
                && (o.CompletedAt ?? o.UpdatedAt) < retentionCutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.RequestFingerprint, (string?)null)
                .SetProperty(o => o.ResponseJson, (string?)null)
                .SetProperty(o => o.FailureMessage, (string?)null)
                .SetProperty(o => o.LastError, (string?)null));

        int purged;
        do
        {
            var expiredIds = await db.SignUpOperations
                .Where(o => o.Status != SignUpOperation.StatusInProgress
                    && (o.Status != SignUpOperation.StatusFailed || o.SweptAt != null)
                    && (o.CompletedAt ?? o.UpdatedAt) < retentionCutoff)
                .OrderBy(o => o.UpdatedAt)
                .Take(PurgeBatchSize)
                .Select(o => o.Id)
                .ToListAsync();

            purged = expiredIds.Count == 0
                ? 0
                : await db.SignUpOperations.Where(o => expiredIds.Contains(o.Id)).ExecuteDeleteAsync();
        }
        while (purged == PurgeBatchSize);
    }
}
