namespace HR.SharedKernel;

public interface IAuditEvent
{
    Guid EventId => Guid.NewGuid();

    AuditActorType ActorType => AuditActorType.Human;

    Guid CompanyId { get; }
    string EventType { get; }
    string EntityType { get; }
    Guid EntityId { get; }
    Guid? EmployeeId => null;
    Guid? ActorUserId { get; }
    Guid? ActorEmployeeId { get; }
    DateTimeOffset OccurredAt { get; }
    Guid? CorrelationId { get; }

    /// <summary>
    /// Ticket 23 (P2): an explicit domain/business workflow identifier (e.g. an offboarding-plan
    /// id), kept deliberately separate from <see cref="CorrelationId"/> (the technical id stable
    /// across the whole request/event chain). Defaults to null so existing events are unaffected;
    /// events tied to a specific long-running business workflow should override this.
    /// </summary>
    Guid? WorkflowId => null;

    string? Summary { get; }
    object? Before { get; }
    object? After { get; }
    object? Metadata { get; }
}
