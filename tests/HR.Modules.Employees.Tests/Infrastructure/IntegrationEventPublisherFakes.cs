using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests.Infrastructure;

public sealed class FakeProbationDateResolver(int months = 6) : IProbationDateResolver
{
    public Task<DateOnly> ResolveEndDateAsync(
        Guid companyId, int? positionMonthsOverride, DateOnly startDate, CancellationToken cancellationToken)
        => Task.FromResult(startDate.AddMonths(positionMonthsOverride ?? months));
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
