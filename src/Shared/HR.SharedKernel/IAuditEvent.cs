namespace HR.SharedKernel;

public interface IAuditEvent
{
    /// <summary>
    /// Stable idempotency key for this event. Used to deduplicate on retry — must be
    /// generated once (e.g. at handler call-site or in the domain event constructor) and
    /// remain the same if the same logical event is re-published.
    /// Default implementation returns a new Guid so existing callers are unaffected; callers
    /// that need stable keys should set this explicitly.
    /// </summary>
    Guid EventId => Guid.NewGuid();

    Guid CompanyId { get; }
    string EventType { get; }
    string EntityType { get; }
    Guid EntityId { get; }
    Guid? EmployeeId => null;
    Guid? ActorUserId { get; }
    Guid? ActorEmployeeId { get; }
    DateTimeOffset OccurredAt { get; }
    Guid? CorrelationId { get; }
    string? Summary { get; }
    object? Before { get; }
    object? After { get; }
    object? Metadata { get; }
}
