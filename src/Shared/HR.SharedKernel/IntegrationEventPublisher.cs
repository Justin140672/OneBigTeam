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
public sealed class IntegrationEventPublisher(IServiceProvider serviceProvider, ILogger<IntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        var handlers = serviceProvider.GetServices<IIntegrationEventHandler<TEvent>>();
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
                    "Integration event handler {HandlerType} failed while handling {EventType}",
                    handler.GetType().Name,
                    typeof(TEvent).Name);
            }
        }
    }
}
