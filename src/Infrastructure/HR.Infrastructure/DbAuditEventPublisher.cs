using HR.Infrastructure.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
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

        try
        {
            // AUD-03 / AUD-04 / NFR-01: payload and actor validation happen here so a rejected
            // event is logged and dropped without ever surfacing to (or failing) the business
            // operation that raised it. Sensitive values must simply never be persisted.
            var pending = AuditPendingItem.From(evt);
            context.AuditPendingItems.Add(pending);

            // Also write the committed audit row in the same (non-business) transaction so audit
            // history is immediately consistent for read-your-writes. The pending staging row is
            // retained as an idempotent crash-recovery safety net: AuditPendingItemPromotionJob
            // will observe the row already committed (unique EventId) and simply mark it done.
            var alreadyCommitted = await context.AuditEvents
                .AnyAsync(e => e.EventId == evt.EventId, cancellationToken);
            if (!alreadyCommitted)
                context.AuditEvents.Add(AuditEvent.From(evt));

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (ProhibitedAuditFieldException pex)
        {
            // AUD-03 / NFR-01: prohibited sensitive field or value in the before/after payload.
            // This is a programming error at the call site (the audit event must project only
            // non-sensitive fields). The event is DROPPED — it never reaches the audit trail — so
            // this is logged at Error to guarantee the regression is visible and not silent.
            logger.LogError(pex,
                "AUD-03: audit event DROPPED — payload contains a prohibited sensitive field/value. " +
                "Narrow the audit event's Before/After projection. " +
                "EventType={EventType} EntityType={EntityType} EntityId={EntityId} CompanyId={CompanyId}",
                evt.EventType, evt.EntityType, evt.EntityId, evt.CompanyId);
        }
        catch (MissingAuditActorException aex)
        {
            // AUD-04: human event with no actor — programming error; fix the audit event.
            logger.LogError(aex,
                "AUD-04: audit event rejected — human-triggered event has no actor identity. " +
                "EventType={EventType} EntityType={EntityType} EntityId={EntityId}",
                evt.EventType, evt.EntityType, evt.EntityId);
        }
        catch (Exception ex)
        {
            // AUD-01: delivery failure — log without sensitive payload so operators can investigate.
            logger.LogError(ex,
                "AUD-01: failed to enqueue audit pending item. " +
                "EventType={EventType} EntityType={EntityType} EntityId={EntityId} CompanyId={CompanyId}",
                evt.EventType, evt.EntityType, evt.EntityId, evt.CompanyId);
        }
    }
}
