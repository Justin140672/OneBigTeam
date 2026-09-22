namespace HR.SharedKernel.ExecutionContext;

/// <summary>
/// AsyncLocal-backed implementation of <see cref="IExecutionContextAccessor"/>. State lives in a
/// static <see cref="AsyncLocal{T}"/> field (the same technique .NET's own
/// <c>HttpContextAccessor</c> uses) so it flows correctly across await points within one logical
/// call chain without needing a DI scope to be threaded through static extension methods (e.g.
/// <c>DbSetAuditOutboxExtensions</c>). Register as a singleton — the instance is stateless; the
/// AsyncLocal field itself provides the per-call-chain isolation.
/// </summary>
public sealed class ExecutionContextAccessor : IExecutionContextAccessor
{
    private static readonly AsyncLocal<IExecutionContext?> AmbientContext = new();

    public IExecutionContext? Current => AmbientContext.Value;

    public IDisposable Push(IExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = AmbientContext.Value;
        AmbientContext.Value = context;
        return new PoppedScope(previous);
    }

    private sealed class PoppedScope(IExecutionContext? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            AmbientContext.Value = previous;
        }
    }
}
