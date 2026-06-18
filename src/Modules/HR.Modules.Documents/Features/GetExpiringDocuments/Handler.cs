using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetExpiringDocuments;

internal sealed class GetExpiringDocumentsHandler(DocumentsDbContext db, IClock clock)
{
    public async Task<Result<GetExpiringDocumentsResponse>> HandleAsync(
        GetExpiringDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var today     = DateOnly.FromDateTime(clock.UtcNow);
        var threshold = today.AddDays(30);

        var rows = await (
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d  in db.Documents.AsNoTracking()     on ed.DocumentId    equals d.Id
            join dt in db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals dt.Id
            where ed.CompanyId   == request.CompanyId
               && ed.ExpiryDate  != null
               && ed.ExpiryDate  <= threshold
            select new
            {
                ed.Id,
                ed.EmployeeId,
                d.Title,
                TypeName   = dt.Name,
                ed.ExpiryDate,
            })
            .OrderBy(x => x.ExpiryDate)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(x => new ExpiringDocumentItem(
                x.Id,
                x.EmployeeId,
                x.Title,
                x.TypeName,
                x.ExpiryDate!.Value,
                x.ExpiryDate < today ? DocumentExpiryStatus.Expired : DocumentExpiryStatus.ExpiringSoon))
            .ToList();

        return Result.Success(new GetExpiringDocumentsResponse(items));
    }
}
