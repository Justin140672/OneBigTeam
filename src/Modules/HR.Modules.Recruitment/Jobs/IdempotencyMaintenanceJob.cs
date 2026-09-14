using Hangfire;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel.Idempotency;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Jobs;

/// <summary>
/// Ticket 3 (P1) follow-up item 4: cleans up expired idempotency records for this module.
/// <see cref="DisableConcurrentExecutionAttribute"/> takes a distributed lock keyed by job id, so
/// an overrun run and the next scheduled tick can never process the same backlog concurrently.
/// </summary>
internal sealed class IdempotencyMaintenanceJob(
    RecruitmentDbContext dbContext,
    ILogger<IdempotencyMaintenanceJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var now = DateTimeOffset.UtcNow;

        await dbContext.IdempotencyRecords.CleanupExpiredIdempotencyRecordsWithLoggingAsync(
            now, logger, "Recruitment", CancellationToken.None);
    }
}
