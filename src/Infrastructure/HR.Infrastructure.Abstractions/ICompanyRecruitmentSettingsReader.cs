namespace HR.Infrastructure.Abstractions;

public interface ICompanyRecruitmentSettingsReader
{
    Task<CompanyRecruitmentSettings> GetRecruitmentSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
