using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.GetAsset;

internal sealed class GetAssetHandler(AssetsDbContext db, AssetResourceAuthorizer authorizer)
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

        if (callerUserId is not { } callerId)
            return Result.Failure<GetAssetResponse>(Error.Forbidden("You do not have permission to view this asset."));

        var assignment = await db.AssetAssignments
            .FirstOrDefaultAsync(aa => aa.AssetId == asset.Id && aa.ReturnedAt == null, cancellationToken);

        var allowed = assignment is null
            ? await authorizer.IsHrAdministratorAsync(callerId, cancellationToken)
            : await authorizer.CanViewAssetAssignmentAsync(
                request.CompanyId, callerId, assignment.EmployeeId, cancellationToken);

        if (!allowed)
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
