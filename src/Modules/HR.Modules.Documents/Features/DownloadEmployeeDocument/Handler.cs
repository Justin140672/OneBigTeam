using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.DownloadEmployeeDocument;

internal sealed class DownloadEmployeeDocumentHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage)
{
    public async Task<Result<Uri>> HandleAsync(
        DownloadEmployeeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var storageKey = await (
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d  in db.Documents.AsNoTracking() on ed.DocumentId equals d.Id
            where ed.Id         == request.EmployeeDocumentId
               && ed.CompanyId  == request.CompanyId
               && ed.EmployeeId == request.EmployeeId
            select d.StorageKey
        ).FirstOrDefaultAsync(cancellationToken);

        if (storageKey is null)
            return Result.Failure<Uri>(Error.NotFound("Employee document was not found."));

        var url = await storage.GetDownloadUrlAsync(storageKey, cancellationToken);
        return Result.Success(url);
    }
}
