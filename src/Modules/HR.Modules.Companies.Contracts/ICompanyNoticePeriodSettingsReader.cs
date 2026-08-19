namespace HR.Modules.Companies.Contracts;

public interface ICompanyNoticePeriodSettingsReader
{
    Task<(NoticePeriodUnit Unit, int Length)> GetDefaultNoticePeriodAsync(Guid companyId, CancellationToken cancellationToken);
}
