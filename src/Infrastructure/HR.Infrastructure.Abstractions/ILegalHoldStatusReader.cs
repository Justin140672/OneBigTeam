namespace HR.Infrastructure.Abstractions;

/// <summary>
/// NFR-07: cross-module read surface used by retention / purge processes (Documents, Recruitment,
/// Notifications, ...) to check whether a company is currently under a legal hold, without
/// referencing HR.Modules.Companies directly. When a company is under legal hold, all automated and
/// operator-triggered retention deletion for that company must be skipped so data is preserved for
/// the duration of the hold. Implemented in HR.Modules.Companies.Services and DI-registered in
/// CompaniesModule (same pattern as ICompanyRecruitmentSettingsReader).
/// </summary>
public interface ILegalHoldStatusReader
{
    Task<bool> IsUnderLegalHoldAsync(Guid companyId, CancellationToken cancellationToken);
}
