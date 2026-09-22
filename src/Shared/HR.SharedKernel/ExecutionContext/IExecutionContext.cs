using HR.SharedKernel;

namespace HR.SharedKernel.ExecutionContext;

/// <summary>
/// Ticket 23 (P2): read-only view of the technical identity of the work currently executing.
/// Application/domain code should only ever consume this interface (via
/// <see cref="IExecutionContextAccessor"/>) — infrastructure alone is responsible for creating and
/// changing it (HTTP middleware, the integration-event publisher, background job/reconciliation
/// entry points).
///
/// Terminology (see specifications ticket 23):
/// - <see cref="CorrelationId"/>: stable across the complete business workflow.
/// - <see cref="MessageId"/>: unique identity of the command/event currently being processed.
/// - <see cref="CausationId"/>: identifies the command/event that directly caused this one — null
///   for the first message in a workflow (e.g. the original HTTP request).
/// - <see cref="TraceId"/>: technical distributed-tracing identity; may be unavailable for delayed
///   or manually retried work, unlike <see cref="CorrelationId"/> which is designed to survive it.
/// A business workflow identifier (e.g. an offboarding-plan id) is a separate, domain-owned
/// concept — see <c>IAuditEvent.WorkflowId</c> — and must never be conflated with CorrelationId.
/// </summary>
public interface IExecutionContext
{
    /// <summary>
    /// Ticket 23 follow-up: a string, not a Guid — the HTTP entry point accepts any caller-supplied
    /// value within a length + character policy, not only well-formed GUIDs. Use
    /// <see cref="CorrelationIdGuid.Derive"/> when a Guid-typed persistence column is required.
    /// </summary>
    string CorrelationId { get; }
    Guid MessageId { get; }
    Guid? CausationId { get; }
    string? TraceId { get; }
    Guid? ActorUserId { get; }
    Guid? ActorEmployeeId { get; }
    AuditActorType ActorType { get; }
    ExecutionOrigin Origin { get; }
}
