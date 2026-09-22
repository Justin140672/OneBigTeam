using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.SharedKernel.Tests;

internal sealed record TestIntegrationEvent : IIntegrationEvent;

internal sealed record OtherIntegrationEvent : IIntegrationEvent;

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

internal sealed class ThrowingRequiredHandler : IRequiredIntegrationEventHandler<TestIntegrationEvent>
{
    public bool Invoked { get; private set; }

    public Task HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        Invoked = true;
        throw new InvalidOperationException("Simulated required handler failure");
    }
}

internal sealed class RecordingRequiredHandler : IRequiredIntegrationEventHandler<TestIntegrationEvent>
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
        var publisher = new IntegrationEventPublisher(provider, logger, new HR.SharedKernel.ExecutionContext.ExecutionContextAccessor());

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
        var publisher = new IntegrationEventPublisher(provider, logger, new HR.SharedKernel.ExecutionContext.ExecutionContextAccessor());

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
        var publisher = new IntegrationEventPublisher(provider, logger, new HR.SharedKernel.ExecutionContext.ExecutionContextAccessor());

        await publisher.PublishAsync(new TestIntegrationEvent(), CancellationToken.None);

        var errorEntry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(nameof(TestIntegrationEvent), errorEntry.Message);
        Assert.Contains(nameof(ThrowingHandler), errorEntry.Message);
        Assert.NotNull(errorEntry.Exception);
    }

    [Fact]
    public async Task PublishAndConfirmAsync_WhenAllHandlersSucceed_ReturnsTrue()
    {
        var recording = new RecordingHandler();
        var recordingRequired = new RecordingRequiredHandler();
        var logger = new SpyLogger<IntegrationEventPublisher>();
        var provider = BuildProvider(logger, recording, recordingRequired);
        var publisher = new IntegrationEventPublisher(provider, logger, new HR.SharedKernel.ExecutionContext.ExecutionContextAccessor());

        var result = await publisher.PublishAndConfirmAsync(new TestIntegrationEvent(), CancellationToken.None);

        Assert.True(result);
        Assert.True(recording.Invoked);
        Assert.True(recordingRequired.Invoked);
    }

    [Fact]
    public async Task PublishAndConfirmAsync_WhenARequiredHandlerThrows_ReturnsFalse()
    {
        var throwingRequired = new ThrowingRequiredHandler();
        var logger = new SpyLogger<IntegrationEventPublisher>();
        var provider = BuildProvider(logger, throwingRequired);
        var publisher = new IntegrationEventPublisher(provider, logger, new HR.SharedKernel.ExecutionContext.ExecutionContextAccessor());

        var result = await publisher.PublishAndConfirmAsync(new TestIntegrationEvent(), CancellationToken.None);

        Assert.False(result);
        Assert.True(throwingRequired.Invoked);
    }

    [Fact]
    public async Task PublishAndConfirmAsync_WhenOnlyANonRequiredHandlerThrows_ReturnsTrue()
    {
        // Best-effort semantics for non-required handlers are unchanged: a plain
        // IIntegrationEventHandler<T> failing must not flip the confirmed-delivery result, only a
        // handler that has explicitly opted in via IRequiredIntegrationEventHandler<T> can do that.
        var throwing = new ThrowingHandler();
        var logger = new SpyLogger<IntegrationEventPublisher>();
        var provider = BuildProvider(logger, throwing);
        var publisher = new IntegrationEventPublisher(provider, logger, new HR.SharedKernel.ExecutionContext.ExecutionContextAccessor());

        var result = await publisher.PublishAndConfirmAsync(new TestIntegrationEvent(), CancellationToken.None);

        Assert.True(result);
        Assert.True(throwing.Invoked);
    }

    [Fact]
    public async Task PublishAndConfirmAsync_WhenARequiredHandlerThrows_OtherRegisteredHandlersStillRun()
    {
        var throwingRequired = new ThrowingRequiredHandler();
        var recording = new RecordingHandler();
        var logger = new SpyLogger<IntegrationEventPublisher>();
        var provider = BuildProvider(logger, throwingRequired, recording);
        var publisher = new IntegrationEventPublisher(provider, logger, new HR.SharedKernel.ExecutionContext.ExecutionContextAccessor());

        var result = await publisher.PublishAndConfirmAsync(new TestIntegrationEvent(), CancellationToken.None);

        Assert.False(result);
        Assert.True(throwingRequired.Invoked);
        Assert.True(recording.Invoked); // never blocked by the required handler's failure
    }

    [Fact]
    public async Task PublishAndConfirmAsync_WhenARequiredHandlerThrows_DoesNotPropagateToCaller()
    {
        var throwingRequired = new ThrowingRequiredHandler();
        var logger = new SpyLogger<IntegrationEventPublisher>();
        var provider = BuildProvider(logger, throwingRequired);
        var publisher = new IntegrationEventPublisher(provider, logger, new HR.SharedKernel.ExecutionContext.ExecutionContextAccessor());

        var exception = await Record.ExceptionAsync(() =>
            publisher.PublishAndConfirmAsync(new TestIntegrationEvent(), CancellationToken.None));

        Assert.Null(exception);
    }

    // ── Ticket 23 (P2): causation chaining ─────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_With_Nothing_Ambient_Mints_A_Root_Context_With_No_Causation()
    {
        var accessor = new ExecutionContextAccessor();
        var capturing = new CapturingHandler(accessor);
        var logger = new SpyLogger<IntegrationEventPublisher>();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OtherIntegrationEvent>>(capturing);
        var provider = services.BuildServiceProvider();
        var publisher = new IntegrationEventPublisher(provider, logger, accessor);

        await publisher.PublishAsync(new OtherIntegrationEvent(), CancellationToken.None);

        Assert.NotNull(capturing.Observed);
        Assert.Equal(capturing.Observed!.MessageId.ToString("D"), capturing.Observed.CorrelationId);
        Assert.Null(capturing.Observed.CausationId);
    }

    [Fact]
    public async Task PublishAsync_Nested_During_Handling_Of_Another_Event_Shares_CorrelationId_And_Chains_CausationId()
    {
        // A handler for TestIntegrationEvent (message A) itself publishes OtherIntegrationEvent
        // (message B) mid-handling. B must carry A's CorrelationId and CausationId == A's MessageId.
        var logger = new SpyLogger<IntegrationEventPublisher>();
        var accessor = new ExecutionContextAccessor();
        var services = new ServiceCollection();

        var capturingOuter = new CapturingHandler(accessor);
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(capturingOuter);

        var capturingInner = new CapturingHandler(accessor);
        services.AddSingleton<IIntegrationEventHandler<OtherIntegrationEvent>>(capturingInner);

        // NestedPublishingHandler needs a reference to the exact publisher instance that will
        // dispatch it, so build the publisher first, then register a handler wrapping it.
        var provider = services.BuildServiceProvider();
        var publisher = new IntegrationEventPublisher(provider, logger, accessor);
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(new NestedPublishingHandler(publisher));
        provider = services.BuildServiceProvider();
        publisher = new IntegrationEventPublisher(provider, logger, accessor);

        await publisher.PublishAsync(new TestIntegrationEvent(), CancellationToken.None);

        Assert.NotNull(capturingOuter.Observed);
        Assert.NotNull(capturingInner.Observed);

        var a = capturingOuter.Observed!;
        var b = capturingInner.Observed!;

        Assert.Equal(a.CorrelationId, b.CorrelationId);
        Assert.Equal(a.MessageId, b.CausationId);
        Assert.NotEqual(a.MessageId, b.MessageId);
    }

    [Fact]
    public async Task DispatchAsync_Restores_Ambient_Context_To_Prior_State_After_Returning()
    {
        var recording = new RecordingHandler();
        var logger = new SpyLogger<IntegrationEventPublisher>();
        var provider = BuildProvider(logger, recording);
        var accessor = new ExecutionContextAccessor();
        var publisher = new IntegrationEventPublisher(provider, logger, accessor);

        var callerContext = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        using (accessor.Push(callerContext))
        {
            await publisher.PublishAsync(new TestIntegrationEvent(), CancellationToken.None);

            // Popped back to whatever was ambient before dispatch started — the caller's own context.
            Assert.Same(callerContext, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    // Captures whatever execution context is ambient (via the same IExecutionContextAccessor
    // instance the publisher under test was constructed with) at the moment it handles an event —
    // this is how a test proves what correlation/causation identity the publisher assigned.
    private sealed class CapturingHandler(IExecutionContextAccessor accessor) :
        IIntegrationEventHandler<TestIntegrationEvent>, IIntegrationEventHandler<OtherIntegrationEvent>
    {
        public IExecutionContext? Observed { get; private set; }

        public Task HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Observed = accessor.Current;
            return Task.CompletedTask;
        }

        public Task HandleAsync(OtherIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Observed = accessor.Current;
            return Task.CompletedTask;
        }
    }

    private sealed class NestedPublishingHandler(IIntegrationEventPublisher publisher) :
        IIntegrationEventHandler<TestIntegrationEvent>
    {
        public async Task HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            await publisher.PublishAsync(new OtherIntegrationEvent(), cancellationToken);
        }
    }
}
