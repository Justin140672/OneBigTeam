using System.Text.Json;
using HR.Infrastructure.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.BackgroundJobs;

/// <summary>
/// AUD-01: promotes <see cref="AuditPendingItem"/> rows from the staging table to the
/// committed <see cref="AuditEvent"/> table.
///
/// Idempotency: <see cref="AuditEvent.EventId"/> carries a unique constraint. If a pending
/// item is processed more than once (e.g. after a crash mid-promotion), the second INSERT
/// throws a unique-constraint violation which is caught and treated as "already committed";
/// the pending row is then marked Committed so it is not retried again.
///
/// Failure visibility: items that fail repeatedly are marked Failed with the error reason.
/// Operators can inspect <c>audit.audit_pending_items WHERE status = 'failed'</c> to identify
/// stuck items and trigger a retry (ResetForRetry).
/// </summary>
internal sealed class AuditPendingItemPromotionJob(
    AuditDbContext context,
    IClock clock,
    ILogger<AuditPendingItemPromotionJob> logger)
{
    private const int BatchSize = 100;

    /// <summary>
    /// Processes up to <see cref="BatchSize"/> pending items per invocation.
    /// Hangfire will re-queue the job if the batch was full (more rows may remain).
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var batch = await context.AuditPendingItems
            .Where(p => p.Status == AuditPendingItem.StatusPending
                     || p.Status == AuditPendingItem.StatusProcessing)
            .OrderBy(p => p.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
            return;

        foreach (var item in batch)
        {
            item.MarkProcessing();
        }
        await context.SaveChangesAsync(cancellationToken);

        foreach (var item in batch)
        {
            await PromoteItemAsync(item, now, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "AUD-01: processed audit pending batch. Total={Total} Committed={Committed} Failed={Failed}",
            batch.Count,
            batch.Count(p => p.Status == AuditPendingItem.StatusCommitted),
            batch.Count(p => p.Status == AuditPendingItem.StatusFailed));
    }

    private async Task PromoteItemAsync(AuditPendingItem item, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            PendingAuditPayload payload;
            try
            {
                payload = JsonSerializer.Deserialize<PendingAuditPayload>(item.PayloadJson)
                    ?? throw new InvalidOperationException("Payload deserialised to null.");
            }
            catch (Exception ex)
            {
                item.MarkFailed($"Payload deserialisation failed: {ex.Message}");
                logger.LogError(ex,
                    "AUD-01: cannot deserialise pending audit item. Id={Id}", item.Id);
                return;
            }

            // Check for existing committed event first (idempotency — no DB round-trip if already done).
            var alreadyCommitted = await context.AuditEvents
                .AnyAsync(e => e.EventId == item.EventId, cancellationToken);

            if (!alreadyCommitted)
            {
                context.AuditEvents.Add(AuditEvent.FromPayload(payload));
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    // Another job instance promoted this event concurrently — that's fine.
                    context.ChangeTracker.Clear();
                    logger.LogDebug(
                        "AUD-01: duplicate promotion detected (concurrent job). EventId={EventId}", item.EventId);
                }
            }

            item.MarkCommitted(now);
        }
        catch (Exception ex)
        {
            item.MarkFailed(ex.Message);
            logger.LogError(ex,
                "AUD-01: failed to promote pending audit item. Id={Id} EventType={EventType}",
                item.Id, TryGetEventType(item.PayloadJson));
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("ix_audit_events_event_id", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) == true;

    private static string TryGetEventType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("EventType", out var v) ? v.GetString() ?? "unknown" : "unknown";
        }
        catch { return "unknown"; }
    }
}
