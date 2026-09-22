using HR.SharedKernel.ExecutionContext;

namespace HR.SharedKernel;

/// <summary>
/// Ticket 23 (P2): common metadata carried alongside an integration event's business payload for
/// durable/cross-process delivery (outbox rows, Hangfire job args), without requiring every
/// <see cref="IIntegrationEvent"/> constructor to grow correlation parameters. Handlers obtain this
/// from the publisher (or reconstruct it via <see cref="ExecutionContextInfo.Restore"/> when
/// resuming durable work); the event payload itself stays business-data-only.
/// </summary>
public sealed record MessageEnvelope(
    Guid MessageId,
    string CorrelationId,
    Guid? CausationId,
    DateTimeOffset OccurredAt,
    string Producer,
    string EventType,
    int SchemaVersion,
    string? TraceId = null,
    Guid? ActorUserId = null,
    Guid? ActorEmployeeId = null,
    AuditActorType ActorType = AuditActorType.Human)
{
    public static MessageEnvelope ForPublish(IExecutionContext context, string producer, string eventType, int schemaVersion = 1) =>
        new(
            context.MessageId,
            context.CorrelationId,
            context.CausationId,
            DateTimeOffset.UtcNow,
            producer,
            eventType,
            schemaVersion,
            context.TraceId,
            context.ActorUserId,
            context.ActorEmployeeId,
            context.ActorType);
}
