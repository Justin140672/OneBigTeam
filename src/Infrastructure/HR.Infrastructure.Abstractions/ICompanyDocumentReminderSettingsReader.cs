namespace HR.Infrastructure.Abstractions;

/// <summary>
/// SET-07: cross-module read surface used by HR.Modules.Documents to read the current company's
/// document expiry reminder schedule without referencing HR.Modules.Companies directly. Implemented
/// in HR.Modules.Companies.Services and DI-registered in CompaniesModule.
/// </summary>
public interface ICompanyDocumentReminderSettingsReader
{
    Task<CompanyDocumentReminderSettings> GetDocumentReminderSettingsAsync(Guid companyId, CancellationToken cancellationToken);
}
