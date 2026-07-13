using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ListCompanyDocumentCategories;

internal sealed class ListCompanyDocumentCategoriesHandler(DocumentsDbContext db)
{
    public async Task<Result<ListCompanyDocumentCategoriesResponse>> HandleAsync(
        ListCompanyDocumentCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.CompanyDocumentCategories
            .AsNoTracking()
            .Where(c => c.CompanyId == request.CompanyId);

        if (!request.IncludeInactive)
            query = query.Where(c => c.IsActive);

        var items = await query
            .OrderBy(c => c.Name)
            .Select(c => new CompanyDocumentCategoryListItem(c.Id, c.Name, c.IsActive))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListCompanyDocumentCategoriesResponse(items));
    }
}
