using HR.SharedKernel;

namespace HR.Modules.Onboarding.Tests.Infrastructure;

internal sealed class CapturingIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly List<IIntegrationEvent> _published = [];
    public IReadOnlyList<IIntegrationEvent> Published => _published;

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        _published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
