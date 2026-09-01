using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Services;

/// <summary>
/// Story 2: cross-module read surface used by HR.Modules.Companies (ExecuteCustomerDeletion) to
/// block platform-admin deletion execution while an organisation data export is being prepared, and
/// by the Reporting request endpoint to reject a duplicate concurrent request. company_id is
/// always enforced explicitly (Reporting has no global query filter).
/// </summary>
internal sealed class OrganisationDataExportStatusReader(ReportingDbContext db)
    : IOrganisationDataExportStatusReader
{
    public Task<bool> HasActiveExportAsync(Guid companyId, CancellationToken cancellationToken) =>
        db.OrganisationDataExports
            .AsNoTracking()
            .AnyAsync(
                e => e.CompanyId == companyId
                     && (e.Status == OrganisationDataExport.StatusPending
                         || e.Status == OrganisationDataExport.StatusInProgress),
                cancellationToken);
}
