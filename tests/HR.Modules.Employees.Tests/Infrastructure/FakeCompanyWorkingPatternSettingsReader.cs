using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="ICompanyWorkingPatternSettingsReader"/>. Defaults to a standard
/// Mon-Fri, 7.5 hours/day pattern (37.5 hours/week), matching Domain.CompanySettings.CreateDefault.
/// </summary>
internal sealed class FakeCompanyWorkingPatternSettingsReader(int workingDayCount = 5, decimal hoursPerDay = 7.5m)
    : ICompanyWorkingPatternSettingsReader
{
    public Task<(int WorkingDayCount, decimal HoursPerDay)> GetDefaultWorkingPatternAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult((workingDayCount, hoursPerDay));
}
