using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed record FileScanBacklog(
    int PendingCount,
    double OldestPendingAgeSeconds,
    int ScanningCount,
    int StaleClaimCount,
    int RetriedCount,
    int MaxAttemptCount,
    int ExhaustedCount,
    int ExhaustedLast24HoursCount);

internal sealed class FileScanBacklogReader(DocumentsDbContext db, IClock clock)
{
    public async Task<FileScanBacklog> ReadAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var work = db.FileScanWork.AsNoTracking();

        var pending = await work.CountAsync(w => w.State == FileScanWorkState.Pending, cancellationToken);
        var oldestPendingCreatedAt = pending == 0
            ? (DateTimeOffset?)null
            : await work.Where(w => w.State == FileScanWorkState.Pending).MinAsync(w => w.CreatedAt, cancellationToken);
        var scanning = await work.CountAsync(w => w.State == FileScanWorkState.Scanning, cancellationToken);
        var stale = await work.CountAsync(
            w => w.State == FileScanWorkState.Scanning && w.LeaseExpiresAt != null && w.LeaseExpiresAt <= now, cancellationToken);
        var retried = await work.CountAsync(
            w => (w.State == FileScanWorkState.Pending || w.State == FileScanWorkState.Scanning) && w.AttemptCount > 1, cancellationToken);
        var maxAttempts = await work.CountAsync(
            w => (w.State == FileScanWorkState.Pending || w.State == FileScanWorkState.Scanning)
                && w.AttemptCount >= FileScanWork.MaxAttempts, cancellationToken);
        var exhausted = await work.CountAsync(w => w.State == FileScanWorkState.Exhausted, cancellationToken);
        var since = now.AddHours(-24);
        var exhaustedRecent = await work.CountAsync(
            w => w.State == FileScanWorkState.Exhausted && w.CompletedAt != null && w.CompletedAt >= since, cancellationToken);

        return new FileScanBacklog(
            pending,
            oldestPendingCreatedAt is { } created ? Math.Max(0, (now - created).TotalSeconds) : 0,
            scanning,
            stale,
            retried,
            maxAttempts,
            exhausted,
            exhaustedRecent);
    }
}
