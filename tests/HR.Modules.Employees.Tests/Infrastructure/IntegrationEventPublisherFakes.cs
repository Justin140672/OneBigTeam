using HR.SharedKernel;

namespace HR.Modules.Employees.Tests.Infrastructure;

public sealed class FakeCompanyProbationSettingsReader(int months = 6) : ICompanyProbationSettingsReader
{
    public Task<int> GetProbationMonthsAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(months);
}

public sealed class NoOpIntegrationEventPublisher : IIntegrationEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
        => Task.CompletedTask;
}

public sealed class CapturingIntegrationEventPublisher : IIntegrationEventPublisher
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
