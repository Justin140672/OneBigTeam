using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.DeactivateDocumentType;

internal sealed class DeactivateDocumentTypeHandler(DocumentsDbContext db, IClock clock)
{
    public async Task<Result> HandleAsync(
        DeactivateDocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var documentType = await db.DocumentTypes
            .SingleOrDefaultAsync(
                dt => dt.Id == request.DocumentTypeId &&
                      dt.CompanyId == request.CompanyId &&
                      dt.IsActive,
                cancellationToken);

        if (documentType is null)
            return Result.Failure(Error.NotFound($"Document type '{request.DocumentTypeId}' was not found."));

        documentType.Deactivate(clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
