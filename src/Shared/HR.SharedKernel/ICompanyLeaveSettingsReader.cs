namespace HR.SharedKernel;

public interface ICompanyLeaveSettingsReader
{
    Task<CompanyLeaveSettings> GetLeaveSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
