using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.UpdateAsset;

internal sealed class UpdateAssetHandler(AssetsDbContext db, IClock clock)
{
    public async Task<Result<UpdateAssetResponse>> HandleAsync(
        UpdateAssetRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await db.Assets
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.CompanyId == request.CompanyId, cancellationToken);

        if (asset is null)
            return Result.Failure<UpdateAssetResponse>(Error.NotFound("Asset not found."));

        var numberConflict = await db.Assets.AnyAsync(
            a => a.CompanyId == request.CompanyId
              && a.AssetNumber == request.AssetNumber
              && a.Id != request.Id,
            cancellationToken);

        if (numberConflict)
            return Result.Failure<UpdateAssetResponse>(
                Error.Conflict($"An asset with number '{request.AssetNumber}' already exists."));

        var categoryExists = await db.AssetCategories.AnyAsync(
            c => c.Id == request.CategoryId && c.CompanyId == request.CompanyId && c.IsActive,
            cancellationToken);

        if (!categoryExists)
            return Result.Failure<UpdateAssetResponse>(Error.NotFound("Asset category not found."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        asset.Update(
            request.AssetNumber,
            request.CategoryId,
            request.Name,
            request.Manufacturer,
            request.Model,
            request.SerialNumber,
            request.PurchaseDate,
            request.PurchasePrice,
            now);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateAssetResponse(
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
            asset.UpdatedAt));
    }
}
