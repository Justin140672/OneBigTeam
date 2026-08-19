using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
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

// Manual mode by default, matching the pre-existing behaviour where every employee number was
// manually supplied — tests that don't care about automatic numbering aren't affected.
public sealed class FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode mode = EmployeeNumberMode.Manual)
    : ICompanyEmployeeNumberSettingsReader
{
    public Task<EmployeeNumberMode> GetModeAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(mode);

    public Task<EmployeeNumberSequencePreview> GetSequencePreviewAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(new EmployeeNumberSequencePreview(null, 1, 1));
}

public sealed class FakeEmployeeNumberGenerator(Func<int, string>? format = null) : IEmployeeNumberGenerator
{
    private int _counter;

    public Task<string> GenerateNextAsync(Guid companyId, CancellationToken cancellationToken)
    {
        _counter++;
        var value = format is null ? $"AUTO-{_counter:D5}" : format(_counter);
        return Task.FromResult(value);
    }
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
