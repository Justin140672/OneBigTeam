namespace HR.Modules.Companies.Contracts;

/// <summary>
/// Read-only projection of a company's standard/default working pattern (CompanySettings.WorkingDays
/// + HoursPerDay). Used to derive a full-time-equivalent Hours Per Week baseline for employees who
/// don't have their own working-pattern override — see callers in HR.Modules.Employees.
/// WorkingDayCount is the number of flagged days in CompanySettings.WorkingDays (0-7), kept as a
/// plain int here (rather than the WorkingDays flags enum) so this contracts project doesn't need a
/// dependency on HR.Infrastructure.Abstractions.
/// </summary>
public interface ICompanyWorkingPatternSettingsReader
{
    Task<(int WorkingDayCount, decimal HoursPerDay)> GetDefaultWorkingPatternAsync(
        Guid companyId, CancellationToken cancellationToken);
}
