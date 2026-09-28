using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.GetAsset;

internal sealed class GetAssetHandler(AssetsDbContext db)
{
    public async Task<Result<GetAssetResponse>> HandleAsync(
        GetAssetRequest request,
        Guid? callerUserId,
        CancellationToken cancellationToken)
    {
        var asset = await db.Assets
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.CompanyId == request.CompanyId, cancellationToken);

        if (asset is null)
            return Result.Failure<GetAssetResponse>(Error.NotFound("Asset not found."));

        // Inline authorization: only the assigned employee can view an asset
        // (HR admin check is handled by endpoint policy; return 404 to hide unauthorized access)
        var assignment = await db.AssetAssignments
            .FirstOrDefaultAsync(aa => aa.AssetId == asset.Id && aa.ReturnedAt == null, cancellationToken);

        if (assignment is not null && assignment.EmployeeId != callerUserId)
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
            categoryName,
            asset.Version));
    }
}
