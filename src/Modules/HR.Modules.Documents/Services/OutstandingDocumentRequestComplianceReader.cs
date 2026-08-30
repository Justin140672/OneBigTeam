using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

/// <summary>
/// ADM-02 Compliance Centre reader for company-wide outstanding employee document requests
/// (status <see cref="DocumentRequestStatus.Requested"/>). Returns the due date and mandatory flag
/// so the coordinating query can bucket each request as overdue / due-soon / informational.
/// </summary>
internal sealed class OutstandingDocumentRequestComplianceReader(DocumentsDbContext db)
    : IOutstandingDocumentRequestComplianceReader
{
    public async Task<IReadOnlyList<OutstandingDocumentRequestComplianceItem>> GetOutstandingDocumentRequestsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from dr in db.DocumentRequests.AsNoTracking()
            join dt in db.DocumentTypes.AsNoTracking() on dr.DocumentTypeId equals dt.Id
            where dr.CompanyId == companyId
               && dr.Status == DocumentRequestStatus.Requested
            select new { dr.Id, dr.EmployeeId, TypeName = dt.Name, dr.DueDate, dr.IsMandatory })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new OutstandingDocumentRequestComplianceItem(
                r.Id, r.EmployeeId, r.TypeName, r.DueDate, r.IsMandatory))
            .OrderBy(r => r.DueDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.EmployeeId)
            .ToList();
    }
}
