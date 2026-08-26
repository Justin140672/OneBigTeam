namespace HR.Infrastructure.Abstractions;

/// <summary>
/// SET-06: cross-module read surface used by HR.Modules.Notifications to read the current
/// company's notification-channel settings without referencing HR.Modules.Companies directly.
/// Implemented in HR.Modules.Companies.Services and DI-registered in CompaniesModule.
/// </summary>
public interface ICompanyNotificationSettingsReader
{
    Task<CompanyNotificationSettings> GetNotificationSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
