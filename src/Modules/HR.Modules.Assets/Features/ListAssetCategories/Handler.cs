using HR.Modules.Assets.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.ListAssetCategories;

internal sealed class ListAssetCategoriesHandler(AssetsDbContext db)
{
    public async Task<List<ListAssetCategoriesResponse>> HandleAsync(
        ListAssetCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.AssetCategories
            .Where(c => c.CompanyId == request.CompanyId);

        if (!request.IncludeInactive)
            query = query.Where(c => c.IsActive);

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new ListAssetCategoriesResponse(
                c.Id, c.CompanyId, c.Name, c.Description,
                c.IsActive, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
