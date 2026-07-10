using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed class OutstandingDocumentRequestReader(DocumentsDbContext dbContext) : IOutstandingDocumentRequestReader
{
    public async Task<IReadOnlyList<OutstandingDocumentRequestItem>> GetOutstandingRequestsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from r in dbContext.DocumentRequests.AsNoTracking()
            join dt in dbContext.DocumentTypes.AsNoTracking() on r.DocumentTypeId equals dt.Id
            where r.CompanyId == companyId
               && r.EmployeeId == employeeId
               && r.Status == DocumentRequestStatus.Requested
            select new OutstandingDocumentRequestItem(
                r.Id,
                dt.Name,
                r.DueDate,
                r.IsMandatory)
        ).ToListAsync(cancellationToken);

        return rows;
    }
}
