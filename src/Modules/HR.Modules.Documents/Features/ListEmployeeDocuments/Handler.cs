using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ListEmployeeDocuments;

internal sealed class ListEmployeeDocumentsHandler(DocumentsDbContext db)
{
    public async Task<Result<ListEmployeeDocumentsResponse>> HandleAsync(
        ListEmployeeDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var query =
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d  in db.Documents.AsNoTracking()     on ed.DocumentId    equals d.Id
            join dt in db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals dt.Id
            where ed.CompanyId  == request.CompanyId
               && ed.EmployeeId == request.EmployeeId
            select new { ed, d, dt };

        if (request.Status.HasValue)
            query = query.Where(x => x.d.Status == request.Status.Value);

        var items = await query
            .OrderByDescending(x => x.ed.CreatedAt)
            .Select(x => new EmployeeDocumentListItem(
                x.ed.Id,
                x.d.Title,
                x.dt.Name,
                x.d.Status,
                x.ed.IssueDate,
                x.ed.ExpiryDate,
                x.ed.AcknowledgedAt != null,
                x.ed.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListEmployeeDocumentsResponse(items));
    }
}
