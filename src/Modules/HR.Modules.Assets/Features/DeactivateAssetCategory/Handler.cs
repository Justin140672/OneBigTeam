using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.DeactivateAssetCategory;

internal sealed class DeactivateAssetCategoryHandler(AssetsDbContext db, IClock clock)
{
    public async Task<Result> HandleAsync(
        DeactivateAssetCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await db.AssetCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.CompanyId == request.CompanyId, cancellationToken);

        if (category is null)
            return Result.Failure(Error.NotFound("Asset category not found."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        category.Deactivate(now);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
