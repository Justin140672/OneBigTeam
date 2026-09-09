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

        var newName = request.Name.Trim();
        if (!string.Equals(category.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            var nameExists = await db.AssetCategories.AnyAsync(
                c => c.CompanyId == request.CompanyId && c.Id != request.Id && c.Name.ToLower() == newName.ToLower(),
                cancellationToken);

            if (nameExists)
            {
                return Result.Failure<UpdateAssetCategoryResponse>(
                    Error.Conflict($"An asset category named '{newName}' already exists."));
            }
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        category.Update(newName, request.Description, now);

        // Ticket 2: optimistic concurrency (base-code helper).
        var saveResult = await db.SaveChangesWithConcurrencyAsync(
            category,
            request.ExpectedVersion,
            "This asset category was changed by someone else since you opened it. Reload the latest details and try again.",
            cancellationToken);

        if (saveResult.IsFailure)
            return Result.Failure<UpdateAssetCategoryResponse>(saveResult.Error);

        return Result.Success(new UpdateAssetCategoryResponse(
            category.Id, category.CompanyId, category.Name, category.Description,
            category.IsActive, category.CreatedAt, category.UpdatedAt, category.Version));
    }
}
