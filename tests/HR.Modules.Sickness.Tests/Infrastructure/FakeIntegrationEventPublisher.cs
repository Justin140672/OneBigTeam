using HR.SharedKernel;

namespace HR.Modules.Sickness.Tests.Infrastructure;

internal sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    public List<object> PublishedEvents { get; } = [];

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        PublishedEvents.Add(integrationEvent!);
        return Task.CompletedTask;
    }
}
