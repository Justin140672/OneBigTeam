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
    IExecutionContext? Current { get; }

    IDisposable Push(IExecutionContext context);
}
