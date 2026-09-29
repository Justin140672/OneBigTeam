namespace HR.Infrastructure.Abstractions;

public interface ICompanyDocumentReminderSettingsReader
{
    Task<CompanyDocumentReminderSettings> GetDocumentReminderSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
