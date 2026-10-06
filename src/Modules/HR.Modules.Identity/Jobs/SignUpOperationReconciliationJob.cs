using Hangfire;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Jobs;

/// <summary>
/// Compensates signup operations abandoned partway through provisioning (process crash, exhausted
/// retries, client never came back): deactivates the company shell and removes the proven-owned
/// Supabase account so the email is free again. Bounded batch; each operation is fenced with a
/// versioned lease before any external side effect.
/// </summary>
internal sealed class SignUpOperationReconciliationJob(
    IdentityDbContext db,
    SignUpOperationCompensator compensator,
    IClock clock,
    ILogger<SignUpOperationReconciliationJob> logger)
{
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan CompensationLease = TimeSpan.FromMinutes(5);
    internal const int BatchSize = 25;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();
        var cutoff = now - StaleAfter;

        var candidateIds = await db.SignUpOperations
            .Where(o => o.Status == SignUpOperation.StatusInProgress
                && o.UpdatedAt < cutoff
                && (o.LeaseExpiresAt == null || o.LeaseExpiresAt < now))
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
                if (operation is null || !operation.IsInProgress)
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
    }
}
