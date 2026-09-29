using HR.SharedKernel.ExecutionContext;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.SharedKernel;

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

        // Security review ticket 7 (P2): cancellation is NOT an ordinary handler failure. Before
        // this fix, OperationCanceledException was caught by the same catch (Exception) block as
        // every other handler exception below, so a cancelled dispatch would: keep running the
        // remaining handlers for the event (each seeing a token that is already cancelled — most
        // will throw immediately anyway, but not all do), and PublishAndConfirmAsync could still
        // report "success" (true) despite the caller's cancellation. A cancellation observed here
        // must stop dispatch immediately and propagate to the caller, exactly like any other
        // cancellable async operation.
        cancellationToken.ThrowIfCancellationRequested();

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
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await handler.HandleAsync(integrationEvent, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Genuine cancellation of the token this dispatch was given — not an ordinary
                    // handler failure. Re-throw so it propagates to the caller and stops dispatch to
                    // any remaining handlers, rather than being isolated/logged/continued like a
                    // normal exception. A handler that throws OperationCanceledException for a
                    // reason UNRELATED to this token (e.g. its own internal, already-disposed token)
                    // would not match this guard and falls through to the ordinary isolation path
                    // below, preserving existing behaviour for that edge case.
                    throw;
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
