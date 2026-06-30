using HR.Modules.Assets.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.ListAssets;

internal sealed class ListAssetsHandler(AssetsDbContext db)
{
    public async Task<List<ListAssetsResponse>> HandleAsync(
        ListAssetsRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.Assets
            .Where(a => a.CompanyId == request.CompanyId);

        if (request.Status.HasValue)
            query = query.Where(a => a.Status == request.Status.Value);

        return await query
            .OrderBy(a => a.AssetNumber)
            .Select(a => new ListAssetsResponse(
                a.Id,
                a.CompanyId,
                a.AssetNumber,
                a.CategoryId,
                a.Name,
                a.Manufacturer,
                a.Model,
                a.SerialNumber,
                a.PurchaseDate,
                a.PurchasePrice,
                a.Status,
                a.CreatedAt,
                a.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
