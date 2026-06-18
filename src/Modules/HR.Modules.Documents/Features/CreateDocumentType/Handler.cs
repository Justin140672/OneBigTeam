using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CreateDocumentType;

internal sealed class CreateDocumentTypeHandler(DocumentsDbContext db, IClock clock)
{
    public async Task<Result<CreateDocumentTypeResponse>> HandleAsync(
        CreateDocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var nameExists = await db.DocumentTypes
            .AnyAsync(
                dt => dt.CompanyId == request.CompanyId &&
                      dt.Name == request.Name.Trim() &&
                      dt.IsActive,
                cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateDocumentTypeResponse>(
                Error.Conflict($"An active document type named '{request.Name.Trim()}' already exists in this company."));
        }

        var now = clock.UtcNowOffset();

        var documentType = DocumentType.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.Name,
            request.Description,
            now);

        db.DocumentTypes.Add(documentType);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateDocumentTypeResponse(
            documentType.Id,
            documentType.CompanyId,
            documentType.Name,
            documentType.Description,
            documentType.IsActive,
            documentType.CreatedAt));
    }
}
