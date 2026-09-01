namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Cross-module read surface used by HR.Modules.Companies (ExecuteCustomerDeletion) to block
/// platform-admin deletion execution while an organisation data export is being prepared.
/// Implemented by an internal service in HR.Modules.Reporting, DI-registered in ReportingModule.
/// </summary>
public interface IOrganisationDataExportStatusReader
{
    /// <summary>True if the company has a Pending or InProgress export.</summary>
    Task<bool> HasActiveExportAsync(Guid companyId, CancellationToken cancellationToken);
}
