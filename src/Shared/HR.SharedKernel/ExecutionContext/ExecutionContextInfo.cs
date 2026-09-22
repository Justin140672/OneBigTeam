using HR.SharedKernel;

namespace HR.SharedKernel.ExecutionContext;

/// <summary>
/// Immutable snapshot implementing <see cref="IExecutionContext"/>. Named "Info" rather than
/// "Context" to avoid ambiguity with <see cref="System.Threading.ExecutionContext"/> when both
/// namespaces are in scope.
///
/// Design note (ticket 23 follow-up): <see cref="CorrelationId"/> is a <see cref="string"/>, not a
/// <see cref="Guid"/> — the HTTP entry point must accept any caller-supplied id within a length +
/// character policy (e.g. "e2e-&lt;guid&gt;"), not only well-formed GUIDs, and echo it back
/// verbatim. <see cref="MessageId"/> and <see cref="CausationId"/> remain <see cref="Guid"/>: they
/// are always minted by this codebase, never caller-supplied, so there is no format constraint to
/// relax. Where a Guid-typed persistence column is required (e.g. <c>IAuditEvent.CorrelationId</c>,
/// the various outbox/operation tables' <c>correlation_id</c> columns), use
/// <see cref="CorrelationIdGuid.Derive"/> to map this string to a stable Guid — see that type for
/// the derivation rule.
/// </summary>
public sealed record ExecutionContextInfo(
    string CorrelationId,
    Guid MessageId,
    Guid? CausationId,
    string? TraceId,
    Guid? ActorUserId,
    Guid? ActorEmployeeId,
    AuditActorType ActorType,
    ExecutionOrigin Origin) : IExecutionContext
{
    /// <summary>
    /// Builds the context for a brand-new workflow root (e.g. a fresh HTTP request or a scheduled
    /// job tick with no prior correlation) — CorrelationId is the string form of a newly minted
    /// MessageId and CausationId is null, since nothing caused this message.
    /// </summary>
    public static ExecutionContextInfo NewRoot(
        ExecutionOrigin origin,
        string? traceId = null,
        Guid? actorUserId = null,
        Guid? actorEmployeeId = null,
        AuditActorType actorType = AuditActorType.Human)
    {
        var id = Guid.NewGuid();
        return new ExecutionContextInfo(id.ToString("D"), id, null, traceId, actorUserId, actorEmployeeId, actorType, origin);
    }

    /// <summary>
    /// Builds the context for a message caused by <paramref name="parent"/>: preserves the parent's
    /// CorrelationId, sets CausationId to the parent's MessageId, and mints a new MessageId.
    /// </summary>
    public static ExecutionContextInfo CausedBy(
        IExecutionContext parent,
        ExecutionOrigin origin,
        string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return new ExecutionContextInfo(
            parent.CorrelationId,
            Guid.NewGuid(),
            parent.MessageId,
            traceId ?? parent.TraceId,
            parent.ActorUserId,
            parent.ActorEmployeeId,
            parent.ActorType,
            origin);
    }

    /// <summary>
    /// Restores a persisted/durable context (e.g. from a stored outbox row or a durable operation
    /// record) after a process restart or on a background retry — reuses the recorded
    /// correlation/causation/message ids rather than inventing new ones, so a retry of the same
    /// logical message keeps its identity. Durable Guid-typed correlation columns pass their value's
    /// string form here (see <see cref="CorrelationIdGuid.Derive"/> for the inverse mapping used when
    /// persisting).
    /// </summary>
    public static ExecutionContextInfo Restore(
        string correlationId,
        Guid messageId,
        Guid? causationId,
        ExecutionOrigin origin,
        Guid? actorUserId = null,
        Guid? actorEmployeeId = null,
        AuditActorType actorType = AuditActorType.IntegrationHandler,
        string? traceId = null) =>
        new(correlationId, messageId, causationId, traceId, actorUserId, actorEmployeeId, actorType, origin);

    /// <summary>
    /// Builds the context for a newly generated recovery/reconciliation action: preserves the
    /// workflow correlation id but mints a new message id and records the recovered
    /// operation/message as its cause.
    /// </summary>
    public static ExecutionContextInfo Recovered(string correlationId, Guid recoveredOperationMessageId) =>
        new(correlationId, Guid.NewGuid(), recoveredOperationMessageId, null, null, null,
            AuditActorType.IntegrationHandler, ExecutionOrigin.ReconciliationJob);
}
