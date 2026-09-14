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
}
