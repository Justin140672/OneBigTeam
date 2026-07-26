using HR.SharedKernel;

namespace HR.Modules.Probation.Tests.Infrastructure;

internal sealed class NoOpIntegrationEventPublisher : IIntegrationEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
        => Task.CompletedTask;
}
