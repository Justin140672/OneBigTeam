using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Services;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests.Infrastructure;

public sealed class FakeProbationDateResolver(int months = 6) : IProbationDateResolver
{
    public Task<DateOnly> ResolveEndDateAsync(
        Guid companyId, int? positionMonthsOverride, DateOnly startDate, CancellationToken cancellationToken)
        => Task.FromResult(startDate.AddMonths(positionMonthsOverride ?? months));
}

// Matches anything by default, so tests that don't care about contact validation aren't affected.
public sealed class FakeCompanyContactValidationReader(
    string postcodeRegex = ".*", string telephoneRegex = ".*", string mobileRegex = ".*")
    : ICompanyContactValidationReader
{
    public Task<CompanyContactValidationRules> GetContactValidationRulesAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(new CompanyContactValidationRules(postcodeRegex, telephoneRegex, mobileRegex));
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
