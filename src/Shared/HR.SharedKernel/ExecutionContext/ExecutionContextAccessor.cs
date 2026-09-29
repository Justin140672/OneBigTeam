namespace HR.SharedKernel.ExecutionContext;

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
