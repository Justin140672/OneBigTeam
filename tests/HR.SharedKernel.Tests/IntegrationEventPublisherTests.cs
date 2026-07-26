using HR.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.SharedKernel.Tests;

internal sealed record TestIntegrationEvent : IIntegrationEvent;

internal sealed class ThrowingHandler : IIntegrationEventHandler<TestIntegrationEvent>
{
    public bool Invoked { get; private set; }

    public Task HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        Invoked = true;
        throw new InvalidOperationException("Simulated handler failure");
    }
}

internal sealed class RecordingHandler : IIntegrationEventHandler<TestIntegrationEvent>
{
    public bool Invoked { get; private set; }

    public Task HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        Invoked = true;
        return Task.CompletedTask;
    }
}

internal sealed class SpyLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

public class IntegrationEventPublisherTests
{
    private static ServiceProvider BuildProvider(SpyLogger<IntegrationEventPublisher> logger, params IIntegrationEventHandler<TestIntegrationEvent>[] handlers)
    {
        var services = new ServiceCollection();
        foreach (var handler in handlers)
            services.AddSingleton(handler);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PublishAsync_WhenOneHandlerThrows_OtherRegisteredHandlersStillRun()
    {
        var throwing = new ThrowingHandler();
        var recording = new RecordingHandler();
        var logger = new SpyLogger<IntegrationEventPublisher>();
        var provider = BuildProvider(logger, throwing, recording);
        var publisher = new IntegrationEventPublisher(provider, logger);

        await publisher.PublishAsync(new TestIntegrationEvent(), CancellationToken.None);

        Assert.True(throwing.Invoked);
        Assert.True(recording.Invoked);
    }

    [Fact]
    public async Task PublishAsync_WhenHandlerThrows_DoesNotPropagateToCaller()
    {
        var throwing = new ThrowingHandler();
        var logger = new SpyLogger<IntegrationEventPublisher>();
        var provider = BuildProvider(logger, throwing);
        var publisher = new IntegrationEventPublisher(provider, logger);

        var exception = await Record.ExceptionAsync(() =>
            publisher.PublishAsync(new TestIntegrationEvent(), CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task PublishAsync_WhenHandlerThrows_LogsFailureWithContext()
    {
        var throwing = new ThrowingHandler();
        var logger = new SpyLogger<IntegrationEventPublisher>();
        var provider = BuildProvider(logger, throwing);
        var publisher = new IntegrationEventPublisher(provider, logger);

        await publisher.PublishAsync(new TestIntegrationEvent(), CancellationToken.None);

        var errorEntry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(nameof(TestIntegrationEvent), errorEntry.Message);
        Assert.Contains(nameof(ThrowingHandler), errorEntry.Message);
        Assert.NotNull(errorEntry.Exception);
    }
}
