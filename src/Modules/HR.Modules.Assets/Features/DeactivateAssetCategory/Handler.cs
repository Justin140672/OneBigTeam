using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.DeactivateAssetCategory;

internal sealed class DeactivateAssetCategoryHandler(
    AssetsDbContext db,
    IClock clock,
    IPositionProfileAssetsReader positionProfileAssetsReader)
{
    public async Task<Result> HandleAsync(
        DeactivateAssetCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await db.AssetCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.CompanyId == request.CompanyId, cancellationToken);

        if (category is null)
            return Result.Failure(Error.NotFound("Asset category not found."));

        var activeAssetCount = await db.Assets
            .CountAsync(
                a => a.CategoryId == request.Id
                  && a.CompanyId == request.CompanyId
                  && a.Status != AssetStatus.Retired,
                cancellationToken);

        var activePositionProfileReferenceCount = await positionProfileAssetsReader
            .CountActiveReferencesToAssetCategoryAsync(request.CompanyId, request.Id, cancellationToken);

        var usageSegments = new List<string>();
        if (activeAssetCount > 0)
            usageSegments.Add($"{activeAssetCount} active asset{(activeAssetCount == 1 ? "" : "s")}");
        if (activePositionProfileReferenceCount > 0)
        {
            usageSegments.Add(
                $"{activePositionProfileReferenceCount} active position profile" +
                $"{(activePositionProfileReferenceCount == 1 ? "" : "s")}");
        }

        if (usageSegments.Count > 0)
        {
            return Result.Failure(Error.Conflict(
                $"Cannot deactivate '{category.Name}' — it is used on " +
                $"{string.Join(" and ", usageSegments)}."));
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        category.Deactivate(now);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
