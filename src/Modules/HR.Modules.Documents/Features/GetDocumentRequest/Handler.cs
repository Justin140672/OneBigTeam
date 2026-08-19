using HR.Modules.Documents.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetDocumentRequest;

internal sealed class GetDocumentRequestHandler(DocumentsDbContext db, IEmployeeNameReader employeeNameReader)
{
    public async Task<Result<GetDocumentRequestResponse>> HandleAsync(
        GetDocumentRequestRequest request,
        CancellationToken cancellationToken)
    {
        var row = await (
            from r  in db.DocumentRequests.AsNoTracking()
            join dt in db.DocumentTypes.AsNoTracking() on r.DocumentTypeId equals dt.Id
            where r.Id         == request.Id
               && r.CompanyId  == request.CompanyId
               && r.EmployeeId == request.EmployeeId
            select new
            {
                r.Id,
                DocumentTypeName = dt.Name,
                r.DueDate,
                Status = r.Status.ToString(),
                r.RequestedByEmployeeId,
                r.CreatedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure<GetDocumentRequestResponse>(
                Error.NotFound($"Document request '{request.Id}' was not found."));

        string? requestedByName = null;
        if (row.RequestedByEmployeeId.HasValue)
        {
            var nameMap = await employeeNameReader.GetNamesAsync(
                request.CompanyId, [row.RequestedByEmployeeId.Value], cancellationToken);
            requestedByName = nameMap.GetValueOrDefault(row.RequestedByEmployeeId.Value);
        }

        return Result.Success(new GetDocumentRequestResponse(
            row.Id,
            row.DocumentTypeName,
            row.DueDate,
            row.Status,
            row.RequestedByEmployeeId,
            requestedByName,
            row.CreatedAt));
    }
}
