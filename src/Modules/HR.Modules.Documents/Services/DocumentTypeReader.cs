using HR.Modules.Documents.Persistence;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed class DocumentTypeReader(DocumentsDbContext dbContext) : IDocumentTypeReader
{
    public Task<bool> ExistsAsync(Guid companyId, Guid documentTypeId, CancellationToken cancellationToken)
        => dbContext.DocumentTypes.AnyAsync(
            dt => dt.Id == documentTypeId && dt.CompanyId == companyId && dt.IsActive,
            cancellationToken);
}
