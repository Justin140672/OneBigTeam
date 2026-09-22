namespace HR.SharedKernel.ExecutionContext;

/// <summary>
/// Ticket 23 (P2): ambient accessor for the current <see cref="IExecutionContext"/>. Mirrors
/// <c>IHttpContextAccessor</c>/<c>ICurrentUser</c> — a scoped/DI-visible read side
/// (<see cref="Current"/>) plus an infrastructure-only mutation side (<see cref="Push"/>) so only
/// infrastructure code (HTTP middleware, the integration-event publisher, background job/
/// reconciliation entry points) ever changes the ambient context; application/domain handlers only
/// ever read it.
/// </summary>
public interface IExecutionContextAccessor
{
    /// <summary>The context for the work currently executing on this logical async call chain, or
    /// null if nothing has established one yet (e.g. code running outside any tracked entry point).</summary>
    IExecutionContext? Current { get; }

    /// <summary>
    /// Establishes <paramref name="context"/> as current for the duration of the returned scope
    /// (and for any async continuation of the calling code), restoring the previous value on
    /// dispose. Nest freely — e.g. a handler processing message A pushes a new context before
    /// publishing message B, and pops back to A's context afterwards.
    /// </summary>
    IDisposable Push(IExecutionContext context);
}
