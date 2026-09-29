namespace HR.Infrastructure.Abstractions;

public interface ICompanyLeaveSettingsReader
{
    Task<CompanyLeaveSettings> GetLeaveSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
