using HR.Modules.Documents.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed class DocumentTypeReader(DocumentsDbContext dbContext) : IDocumentTypeReader
{
    public Task<bool> ExistsAsync(Guid companyId, Guid documentTypeId, CancellationToken cancellationToken)
        => dbContext.DocumentTypes.AnyAsync(
            dt => dt.Id == documentTypeId && dt.CompanyId == companyId && dt.IsActive,
            cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId,
        IEnumerable<Guid> documentTypeIds,
        CancellationToken cancellationToken)
    {
        var ids = documentTypeIds.ToList();
        return await dbContext.DocumentTypes
            .Where(dt => dt.CompanyId == companyId && ids.Contains(dt.Id))
            .ToDictionaryAsync(dt => dt.Id, dt => dt.Name, cancellationToken);
    }
}
