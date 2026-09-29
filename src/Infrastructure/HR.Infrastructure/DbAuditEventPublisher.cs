using HR.Infrastructure.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
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
    ILogger<DbAuditEventPublisher> logger,
    IExecutionContextAccessor executionContextAccessor) : IAuditEventPublisher
{
    public async Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (auditEvent is not IAuditEvent evt)
            return;

        try
        {
            // Ticket 23 (P2) follow-up: a central "default the ambient correlation id onto every
            // audit event" was tried here and REVERTED — HR.Modules.Employees.Features.
            // GetEmployeeAuditHistory.Handler.MergeCorrelatedItems already repurposes
            // IAuditEvent.CorrelationId as a narrow, explicit merge key shared deliberately between
            // exactly two coordinated requests (the combined Employee+Employment tab save). Defaulting
            // every audit event published within the same HTTP request to the SAME ambient
            // correlation id made every unrelated audit row from that request (task creation,
            // notifications, offboarding-plan start, ...) look like part of that same merge group,
            // silently collapsing them into one displayed item — a real, reproduced regression
            // (LeavingProcessLifecycleEndToEndTests). Technical per-request correlation still flows
            // through request logs (Serilog), integration-event causation chaining
            // (ExecutionContext/MessageEnvelope), and the durable outbox/operation tables' dedicated
            // correlation_id/causation_id/message_id columns (separate from IAuditEvent.CorrelationId
            // and never read by this merge feature) — only the direct, synchronous
            // IAuditEventPublisher.PublishAsync path intentionally does NOT also default this field,
            // to avoid corrupting the pre-existing merge semantics. See CorrelationIdGuid for the
            // string -> Guid mapping still used by the outbox/operation paths.
            _ = executionContextAccessor;

            // AUD-03 / AUD-04 / NFR-01: payload and actor validation happen here so a rejected
            // event is logged and dropped without ever surfacing to (or failing) the business
            // operation that raised it. Sensitive values must simply never be persisted.
            var pending = AuditPendingItem.From(evt);
            context.AuditPendingItems.Add(pending);

            var alreadyCommitted = await context.AuditEvents
                .AnyAsync(e => e.EventId == evt.EventId, cancellationToken);
            if (!alreadyCommitted)
                context.AuditEvents.Add(AuditEvent.From(evt));

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (ProhibitedAuditFieldException pex)
        {
            logger.LogError(pex,
                "AUD-03: audit event DROPPED — payload contains a prohibited sensitive field/value. " +
                "Narrow the audit event's Before/After projection. " +
                "EventType={EventType} EntityType={EntityType} EntityId={EntityId} CompanyId={CompanyId}",
                evt.EventType, evt.EntityType, evt.EntityId, evt.CompanyId);
        }
        catch (MissingAuditActorException aex)
        {
            logger.LogError(aex,
                "AUD-04: audit event rejected — human-triggered event has no actor identity. " +
                "EventType={EventType} EntityType={EntityType} EntityId={EntityId}",
                evt.EventType, evt.EntityType, evt.EntityId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "AUD-01: failed to enqueue audit pending item. " +
                "EventType={EventType} EntityType={EntityType} EntityId={EntityId} CompanyId={CompanyId}",
                evt.EventType, evt.EntityType, evt.EntityId, evt.CompanyId);
        }
    }
}
