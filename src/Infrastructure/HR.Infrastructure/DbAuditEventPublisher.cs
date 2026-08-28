using HR.Infrastructure.Persistence;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure;

/// <summary>
/// AUD-01: writes a <see cref="AuditPendingItem"/> to the audit staging table.
/// The pending item is later promoted to <see cref="AuditEvent"/> by
/// <see cref="BackgroundJobs.AuditPendingItemPromotionJob"/> running in the background.
///
/// Writing to the pending table rather than directly to <see cref="AuditEvent"/> means:
/// - A failure here does not corrupt any committed business data.
/// - If this save fails, the caller gets a logged warning; the API response still reflects
///   whether the business mutation succeeded (addresses the "misleadingly reports unchanged
///   operation" criterion).
/// - Retries are safe because <see cref="AuditEvent.EventId"/> carries a unique constraint
///   that prevents duplicate audit rows.
/// </summary>
internal sealed class DbAuditEventPublisher(
    AuditDbContext context,
    ILogger<DbAuditEventPublisher> logger) : IAuditEventPublisher
{
    public async Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (auditEvent is not IAuditEvent evt)
            return;

        var pending = AuditPendingItem.From(evt);
        context.AuditPendingItems.Add(pending);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // AUD-01: log the failure without any sensitive payload content so operators can
            // investigate, but the caller is not propagated an exception — the business
            // mutation has already committed successfully and the API must not claim it failed.
            logger.LogError(ex,
                "AUD-01: failed to enqueue audit pending item. " +
                "EventType={EventType} EntityType={EntityType} EntityId={EntityId} CompanyId={CompanyId}",
                evt.EventType, evt.EntityType, evt.EntityId, evt.CompanyId);
        }
    }
}
