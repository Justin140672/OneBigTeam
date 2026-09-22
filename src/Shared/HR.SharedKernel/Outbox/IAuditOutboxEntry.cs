namespace HR.SharedKernel.Outbox;

/// <summary>
/// Ticket 3 (P1) follow-up item 5: contract for a module's own internal audit-outbox entity -
/// mirrors the per-module <c>IIdempotencyRecord</c> convention (see that type for why a shared
/// public entity type isn't used). A row is staged in the SAME SaveChangesAsync call as the business
/// mutation and its idempotency record, so all three commit atomically - a crash after commit can
/// never lose the "publish this audit event" intent, only delay it. A background dispatcher (see
/// <see cref="DbSetAuditOutboxExtensions.DispatchPendingAsync{TEntry}"/>) delivers the event via the
/// existing <see cref="IAuditEventPublisher"/> independently, retrying with backoff until it
/// succeeds or is marked terminally failed for operator attention.
/// </summary>
public interface IAuditOutboxEntry
{
    Guid Id { get; set; }

    /// <summary>
    /// Which publisher redelivers this entry: <see cref="OutboxChannel.Audit"/> (via
    /// <c>IAuditEventPublisher</c>) or <see cref="OutboxChannel.Integration"/> (via
    /// <c>IIntegrationEventPublisher</c>, for cross-module events such as employee creation whose
    /// downstream onboarding/probation/leave/task/notification work must survive a process crash
    /// between commit and delivery). Despite the name, this one table/entity now carries both -
    /// renaming it repo-wide wasn't worth the churn once integration events needed the same
    /// atomicity guarantee.
    /// </summary>
    string Channel { get; set; }

    /// <summary>Assembly-qualified CLR type name of the staged event, for redelivery.</summary>
    string EventTypeName { get; set; }

    string PayloadJson { get; set; }

    Guid CompanyId { get; set; }

    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset? DispatchedAt { get; set; }

    int AttemptCount { get; set; }

    DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>Exception message only - never the event payload, which may hold sensitive data.</summary>
    string? LastError { get; set; }

    /// <summary>Set once <see cref="AttemptCount"/> exceeds the dispatcher's retry ceiling.</summary>
    bool IsTerminallyFailed { get; set; }

    /// <summary>
    /// Ticket 23 (P2): the workflow correlation id in effect when this entry was staged, captured
    /// from the ambient execution context. Nullable so rows written before this column existed
    /// remain dispatchable — see <see cref="DbSetAuditOutboxExtensions.DispatchPendingAsync{TEntry}"/>.
    /// </summary>
    Guid? CorrelationId { get; set; }

    /// <summary>Ticket 23 (P2): the message id of the command/event that directly caused this one
    /// to be staged, or null if this entry is a workflow root (e.g. staged directly from an HTTP
    /// command handler with nothing ambient).</summary>
    Guid? CausationId { get; set; }

    /// <summary>Ticket 23 (P2): this entry's own stable message id — distinct from the deterministic
    /// event id some events already carry for retry-deduplication (<c>IAuditEvent.EventId</c>,
    /// left completely unchanged by this ticket).</summary>
    Guid? MessageId { get; set; }
}
