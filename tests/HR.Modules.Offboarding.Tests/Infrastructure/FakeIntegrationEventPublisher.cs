using HR.SharedKernel;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    public List<object> PublishedEvents { get; } = [];

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        PublishedEvents.Add(integrationEvent!);
        return Task.CompletedTask;
    }

    public async Task<bool> PublishAndConfirmAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        await PublishAsync(integrationEvent, cancellationToken);
        return true;
    }
}
