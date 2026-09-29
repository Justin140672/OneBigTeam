using System.Diagnostics;
using System.Threading;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Integration.Tests.Performance;

internal sealed class QueryCountingInterceptor : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    public static readonly QueryCountingInterceptor Instance = new();

    public static readonly TimeSpan SlowCommandThreshold = TimeSpan.FromMilliseconds(200);

    private const string CommandExecutedEvent = "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted";

    private static readonly object Gate = new();
    private static QueryCountState? _current;
    private int _subscribed;

    private QueryCountingInterceptor()
    {
    }

    public static IDisposable BeginScope()
    {
        Instance.EnsureSubscribed();
        lock (Gate)
        {
            var previous = _current;
            _current = new QueryCountState();
            return new QueryCountScope(previous);
        }
    }

    public static QueryCountState? ActiveState
    {
        get
        {
            lock (Gate)
            {
                return _current;
            }
        }
    }

    private void EnsureSubscribed()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
        {
            DiagnosticListener.AllListeners.Subscribe(this);
        }
    }

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener value)
    {
        if (value.Name == "Microsoft.EntityFrameworkCore")
        {
            value.Subscribe(this);
        }
    }

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Key != CommandExecutedEvent || value.Value is not CommandExecutedEventData data)
        {
            return;
        }

        lock (Gate)
        {
            if (_current is null)
            {
                return;
            }

            _current.CommandCount++;
            if (data.Duration >= SlowCommandThreshold)
            {
                _current.SlowCommands.Add(new SlowCommand(data.Command.CommandText, data.Duration));
            }
        }
    }

    void IObserver<DiagnosticListener>.OnCompleted() { }
    void IObserver<DiagnosticListener>.OnError(Exception error) { }
    void IObserver<KeyValuePair<string, object?>>.OnCompleted() { }
    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { }

    private sealed class QueryCountScope : IDisposable
    {
        private readonly QueryCountState? _previous;

        public QueryCountScope(QueryCountState? previous) => _previous = previous;

        public void Dispose()
        {
            lock (Gate)
            {
                _current = _previous;
            }
        }
    }
}

internal sealed class QueryCountState
{
    public int CommandCount;

    public List<SlowCommand> SlowCommands { get; } = new();
}

internal readonly record struct SlowCommand(string CommandText, TimeSpan Duration);
