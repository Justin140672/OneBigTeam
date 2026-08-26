namespace HR.Infrastructure.Abstractions;

/// <summary>
/// SET-05: cross-module read surface used by HR.Modules.Recruitment to read the current company's
/// vacancy/offer approval requirements and candidate retention window, without referencing
/// HR.Modules.Companies directly. Implemented in HR.Modules.Companies.Services and DI-registered in
/// CompaniesModule (same pattern as ICompanySicknessSettingsReader).
/// </summary>
public interface ICompanyRecruitmentSettingsReader
{
    Task<CompanyRecruitmentSettings> GetRecruitmentSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
