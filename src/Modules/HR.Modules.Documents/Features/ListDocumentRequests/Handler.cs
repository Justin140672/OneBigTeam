using HR.Modules.Documents.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ListDocumentRequests;

internal sealed class ListDocumentRequestsHandler(DocumentsDbContext db, IEmployeeNameReader employeeNameReader)
{
    public async Task<Result<ListDocumentRequestsResponse>> HandleAsync(
        ListDocumentRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from r  in db.DocumentRequests.AsNoTracking()
            join dt in db.DocumentTypes.AsNoTracking() on r.DocumentTypeId equals dt.Id
            where r.CompanyId  == request.CompanyId
               && r.EmployeeId == request.EmployeeId
            orderby r.CreatedAt descending
            select new
            {
                r.Id,
                DocumentTypeName       = dt.Name,
                r.DueDate,
                Status                 = r.Status.ToString(),
                r.RequestedByEmployeeId,
                r.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var requesterIds = rows
            .Where(r => r.RequestedByEmployeeId.HasValue)
            .Select(r => r.RequestedByEmployeeId!.Value)
            .Distinct();

        var nameMap = await employeeNameReader.GetNamesAsync(
            request.CompanyId, requesterIds, cancellationToken);

        var items = rows.Select(r => new DocumentRequestListItem(
            r.Id,
            r.DocumentTypeName,
            r.DueDate,
            r.Status,
            r.RequestedByEmployeeId,
            r.RequestedByEmployeeId.HasValue
                ? nameMap.GetValueOrDefault(r.RequestedByEmployeeId.Value)
                : null,
            r.CreatedAt)).ToList();

        return Result.Success(new ListDocumentRequestsResponse(items));
    }
}
