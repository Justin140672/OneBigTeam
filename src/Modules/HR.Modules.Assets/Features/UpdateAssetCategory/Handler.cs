using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.UpdateAssetCategory;

internal sealed class UpdateAssetCategoryHandler(AssetsDbContext db, IClock clock)
{
    public async Task<Result<UpdateAssetCategoryResponse>> HandleAsync(
        UpdateAssetCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await db.AssetCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.CompanyId == request.CompanyId, cancellationToken);

        if (category is null)
            return Result.Failure<UpdateAssetCategoryResponse>(Error.NotFound("Asset category not found."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        category.Update(request.Name, request.Description, now);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateAssetCategoryResponse(
            category.Id, category.CompanyId, category.Name, category.Description,
            category.IsActive, category.CreatedAt, category.UpdatedAt));
    }
}
