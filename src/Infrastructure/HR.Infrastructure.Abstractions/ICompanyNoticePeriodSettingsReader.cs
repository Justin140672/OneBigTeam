namespace HR.Infrastructure.Abstractions;

public interface ICompanyNoticePeriodSettingsReader
{
    Task<(NoticePeriodUnit Unit, int Length)> GetDefaultNoticePeriodAsync(Guid companyId, CancellationToken cancellationToken);
}
