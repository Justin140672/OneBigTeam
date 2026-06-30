using HR.Modules.Assets.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.ListAssetCategories;

internal sealed class ListAssetCategoriesHandler(AssetsDbContext db)
{
    public async Task<List<ListAssetCategoriesResponse>> HandleAsync(
        ListAssetCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        return await db.AssetCategories
            .Where(c => c.CompanyId == request.CompanyId && c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new ListAssetCategoriesResponse(
                c.Id, c.CompanyId, c.Name, c.Description,
                c.IsActive, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
