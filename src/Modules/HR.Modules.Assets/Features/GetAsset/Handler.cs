using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.GetAsset;

internal sealed class GetAssetHandler(AssetsDbContext db)
{
    public async Task<Result<GetAssetResponse>> HandleAsync(
        GetAssetRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await db.Assets
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.CompanyId == request.CompanyId, cancellationToken);

        if (asset is null)
            return Result.Failure<GetAssetResponse>(Error.NotFound("Asset not found."));

        var categoryName = await db.AssetCategories
            .Where(c => c.Id == asset.CategoryId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(new GetAssetResponse(
            asset.Id,
            asset.CompanyId,
            asset.AssetNumber,
            asset.CategoryId,
            asset.Name,
            asset.Manufacturer,
            asset.Model,
            asset.SerialNumber,
            asset.PurchaseDate,
            asset.PurchasePrice,
            asset.Status.ToString(),
            asset.CreatedAt,
            asset.UpdatedAt,
            categoryName));
    }
}
