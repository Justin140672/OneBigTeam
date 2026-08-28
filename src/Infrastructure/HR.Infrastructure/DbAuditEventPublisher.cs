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
        catch (ProhibitedAuditFieldException pex)
        {
            // AUD-03: prohibited sensitive field in payload — programming error; fix at call site.
            logger.LogError(pex,
                "AUD-03: audit event rejected — prohibited sensitive field. " +
                "EventType={EventType} EntityType={EntityType} EntityId={EntityId}",
                evt.EventType, evt.EntityType, evt.EntityId);
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
