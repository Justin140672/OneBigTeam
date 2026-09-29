using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeCompanyWorkingPatternSettingsReader(int workingDayCount = 5, decimal hoursPerDay = 7.5m)
    : ICompanyWorkingPatternSettingsReader
{
    public Task<(int WorkingDayCount, decimal HoursPerDay)> GetDefaultWorkingPatternAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult((workingDayCount, hoursPerDay));
}
