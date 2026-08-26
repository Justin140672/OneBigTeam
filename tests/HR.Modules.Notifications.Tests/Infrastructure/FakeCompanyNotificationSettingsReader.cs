using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeCompanyNotificationSettingsReader(CompanyNotificationSettings? settings = null)
    : ICompanyNotificationSettingsReader
{
    public Task<CompanyNotificationSettings> GetNotificationSettingsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(settings ?? CompanyNotificationSettings.Default);
}
