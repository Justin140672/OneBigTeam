using Microsoft.Extensions.DependencyInjection;

namespace HR.SharedKernel;
public sealed class IntegrationEventPublisher(IServiceProvider serviceProvider) : IIntegrationEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        var handlers = serviceProvider.GetServices<IIntegrationEventHandler<TEvent>>();
        foreach (var handler in handlers)
            await handler.HandleAsync(integrationEvent, cancellationToken);
    }
}
