namespace HR.Modules.Companies.Contracts;

public interface ICompanyWorkingPatternSettingsReader
{
    Task<(int WorkingDayCount, decimal HoursPerDay)> GetDefaultWorkingPatternAsync(
        Guid companyId, CancellationToken cancellationToken);
}
