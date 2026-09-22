using HR.SharedKernel.ExecutionContext;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.SharedKernel;

// A single handler failing must never prevent other handlers for the same integration event
// from running, and must never propagate back to the publishing caller (which is typically
// mid-way through committing an unrelated business transaction). Each handler invocation is
// isolated in its own try/catch; failures are logged with enough context to diagnose (event
// type, handler type, exception) and the loop continues. This changes behaviour for every
// existing integration event: a handler failure no longer aborts publication to remaining
// handlers or bubbles up to the caller.
public sealed class IntegrationEventPublisher(
    IServiceProvider serviceProvider,
    ILogger<IntegrationEventPublisher> logger,
    IExecutionContextAccessor executionContextAccessor) : IIntegrationEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        await DispatchAsync(integrationEvent, envelopeContext: null, cancellationToken);
    }

    public async Task<bool> PublishAndConfirmAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        return await DispatchAsync(integrationEvent, envelopeContext: null, cancellationToken);
    }

    /// <summary>
    /// Ticket 23 (P2) compatibility overload for durable redelivery (outbox dispatch after a
    /// process restart, background retry, reconciliation): restores <paramref name="restoredContext"/>
    /// (built via <see cref="ExecutionContextInfo.Restore"/> from persisted metadata) as the ambient
    /// execution context for the duration of this publish, instead of deriving one from whatever
    /// happens to be ambient. A retry of the same logical message therefore keeps its original
    /// message/correlation/causation ids.
    /// </summary>
    public async Task<bool> PublishAndConfirmAsync<TEvent>(
        TEvent integrationEvent, IExecutionContext restoredContext, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(restoredContext);
        return await DispatchAsync(integrationEvent, restoredContext, cancellationToken);
    }

    // allRequiredSucceeded starts true and is only ever flipped to false, so PublishAsync (which
    // ignores the return value) and PublishAndConfirmAsync share this single dispatch loop with no
    // behavioural difference for non-required handlers.
    private async Task<bool> DispatchAsync<TEvent>(
        TEvent integrationEvent, IExecutionContext? envelopeContext, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        // Ticket 23 (P2) causation chaining: the context for handlers of THIS event is either the
        // explicitly restored one (durable redelivery), or derived from whatever is ambient right
        // now — if a handler for message A is what's calling PublishAsync for message B, "ambient"
        // is A's context, so B's CausationId becomes A's MessageId while B keeps A's CorrelationId.
        // If nothing is ambient (e.g. published directly outside any tracked entry point), a new
        // root context is minted so downstream handlers still see a consistent correlation id.
        var parentOrAmbient = envelopeContext ?? executionContextAccessor.Current;
        var eventContext = envelopeContext is not null
            ? envelopeContext
            : parentOrAmbient is not null
                ? ExecutionContextInfo.CausedBy(parentOrAmbient, ExecutionOrigin.IntegrationEvent)
                : ExecutionContextInfo.NewRoot(ExecutionOrigin.IntegrationEvent);

        var handlers = serviceProvider.GetServices<IIntegrationEventHandler<TEvent>>();
        var allRequiredSucceeded = true;

        using (executionContextAccessor.Push(eventContext))
        using (logger.BeginScope(new Dictionary<string, object?>
               {
                   ["CorrelationId"] = eventContext.CorrelationId,
                   ["MessageId"] = eventContext.MessageId,
                   ["CausationId"] = eventContext.CausationId,
               }))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(integrationEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Integration event handler {HandlerType} failed while handling {EventType}. " +
                        "CorrelationId={CorrelationId} MessageId={MessageId} CausationId={CausationId}",
                        handler.GetType().Name,
                        typeof(TEvent).Name,
                        eventContext.CorrelationId,
                        eventContext.MessageId,
                        eventContext.CausationId);

                    if (handler is IRequiredIntegrationEventHandler<TEvent>)
                        allRequiredSucceeded = false;
                }
            }
        }

        return allRequiredSucceeded;
    }
}
