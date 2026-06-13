namespace HR.SharedKernel;

public interface IAuditEvent
{
    Guid CompanyId { get; }
    string EventType { get; }
    string EntityType { get; }
    Guid EntityId { get; }
    Guid? ActorUserId { get; }
    Guid? ActorEmployeeId { get; }
    DateTimeOffset OccurredAt { get; }
    Guid? CorrelationId { get; }
    string? Summary { get; }
    object? Before { get; }
    object? After { get; }
    object? Metadata { get; }
}
