using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UpdateDocumentType;

internal sealed class UpdateDocumentTypeHandler(DocumentsDbContext db, IClock clock)
{
    public async Task<Result<UpdateDocumentTypeResponse>> HandleAsync(
        UpdateDocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var documentType = await db.DocumentTypes
            .SingleOrDefaultAsync(
                dt => dt.Id == request.DocumentTypeId &&
                      dt.CompanyId == request.CompanyId &&
                      dt.IsActive,
                cancellationToken);

        if (documentType is null)
        {
            return Result.Failure<UpdateDocumentTypeResponse>(
                Error.NotFound($"Document type '{request.DocumentTypeId}' was not found."));
        }

        var newName = request.Name.Trim();
        if (!string.Equals(documentType.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            var nameExists = await db.DocumentTypes
                .AnyAsync(
                    dt => dt.CompanyId == request.CompanyId &&
                          dt.Id != request.DocumentTypeId &&
                          dt.Name.ToLower() == newName.ToLower() &&
                          dt.IsActive,
                    cancellationToken);

            if (nameExists)
            {
                return Result.Failure<UpdateDocumentTypeResponse>(
                    Error.Conflict($"An active document type named '{newName}' already exists in this company."));
            }
        }

        var now = clock.UtcNowOffset();

        documentType.Update(newName, request.Description, request.AllowEmployeeUpload, now);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateDocumentTypeResponse(
            documentType.Id,
            documentType.CompanyId,
            documentType.Name,
            documentType.Description,
            documentType.IsActive,
            documentType.AllowEmployeeUpload,
            documentType.UpdatedAt));
    }
}
