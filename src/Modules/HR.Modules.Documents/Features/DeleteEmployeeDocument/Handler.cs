using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.DeleteEmployeeDocument;

internal sealed class DeleteEmployeeDocumentHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage)
{
    public async Task<Result> HandleAsync(
        DeleteEmployeeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var employeeDocument = await db.EmployeeDocuments
            .SingleOrDefaultAsync(
                ed => ed.Id         == request.EmployeeDocumentId &&
                      ed.CompanyId  == request.CompanyId &&
                      ed.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (employeeDocument is null)
            return Result.Failure(Error.NotFound("Employee document was not found."));

        var document = await db.Documents
            .SingleAsync(d => d.Id == employeeDocument.DocumentId, cancellationToken);

        db.EmployeeDocuments.Remove(employeeDocument);

        var otherLinks = await db.EmployeeDocuments
            .AnyAsync(
                ed => ed.DocumentId == document.Id &&
                      ed.Id         != employeeDocument.Id,
                cancellationToken);

        string? storageKeyToDelete = null;

        if (!otherLinks)
        {
            storageKeyToDelete = document.StorageKey;
            db.Documents.Remove(document);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (storageKeyToDelete is not null)
            await storage.DeleteAsync(storageKeyToDelete, cancellationToken);

        return Result.Success();
    }
}
