using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ListDocumentTypes;

internal sealed class ListDocumentTypesHandler(DocumentsDbContext db)
{
    public async Task<Result<ListDocumentTypesResponse>> HandleAsync(
        ListDocumentTypesRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.DocumentTypes
            .AsNoTracking()
            .Where(dt => dt.CompanyId == request.CompanyId);

        if (!request.IncludeInactive)
            query = query.Where(dt => dt.IsActive);

        var items = await query
            .OrderBy(dt => dt.Name)
            .Select(dt => new DocumentTypeListItem(dt.Id, dt.Name, dt.Description, dt.IsActive))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListDocumentTypesResponse(items));
    }
}
