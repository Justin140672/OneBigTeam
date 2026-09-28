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

        // Inline resource-level authorization: verify caller is authorized to view this asset.
        // Only the assigned employee (if assigned) can view the asset. HR admins and managers
        // are checked at the endpoint level via AssetResourceAuthorizer (to be integrated).
        // For now, unassigned assets are only viewable by HR admins (via policy), and attempting
        // to view an assigned asset you don't own returns Forbidden.
        var assignment = await db.AssetAssignments
            .FirstOrDefaultAsync(aa => aa.AssetId == asset.Id && aa.ReturnedAt == null, cancellationToken);

        // If asset is assigned and caller is not the assigned employee, deny access
        if (assignment is not null && assignment.EmployeeId != callerUserId)
            return Result.Failure<GetAssetResponse>(Error.Forbidden("You do not have permission to view this asset."));

        // If asset is unassigned, deny access (only HR admin can view unassigned assets, checked via policy)
        if (assignment is null)
            return Result.Failure<GetAssetResponse>(Error.Forbidden("You do not have permission to view this asset."));

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
