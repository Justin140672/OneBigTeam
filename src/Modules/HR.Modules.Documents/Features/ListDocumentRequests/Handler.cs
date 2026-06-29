using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ListDocumentRequests;

internal sealed class ListDocumentRequestsHandler(DocumentsDbContext db)
{
    public async Task<Result<ListDocumentRequestsResponse>> HandleAsync(
        ListDocumentRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var items = await (
            from r  in db.DocumentRequests.AsNoTracking()
            join dt in db.DocumentTypes.AsNoTracking() on r.DocumentTypeId equals dt.Id
            where r.CompanyId  == request.CompanyId
               && r.EmployeeId == request.EmployeeId
            orderby r.CreatedAt descending
            select new DocumentRequestListItem(
                r.Id,
                dt.Name,
                r.DueDate,
                r.Status.ToString()))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListDocumentRequestsResponse(items));
    }
}
