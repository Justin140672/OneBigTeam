namespace HR.Infrastructure.Abstractions;

public interface ICompanyNotificationSettingsReader
{
    Task<CompanyNotificationSettings> GetNotificationSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
