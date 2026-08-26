using HR.Infrastructure.Abstractions;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeCompanyRecruitmentSettingsReader(CompanyRecruitmentSettings? settings = null)
    : ICompanyRecruitmentSettingsReader
{
    public Task<CompanyRecruitmentSettings> GetRecruitmentSettingsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(settings ?? CompanyRecruitmentSettings.Default);
}
