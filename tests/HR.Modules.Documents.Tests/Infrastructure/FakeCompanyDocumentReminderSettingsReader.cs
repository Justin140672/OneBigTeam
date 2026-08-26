using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeCompanyDocumentReminderSettingsReader(CompanyDocumentReminderSettings? settings = null)
    : ICompanyDocumentReminderSettingsReader
{
    public Task<CompanyDocumentReminderSettings> GetDocumentReminderSettingsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(settings ?? CompanyDocumentReminderSettings.Default);
}
