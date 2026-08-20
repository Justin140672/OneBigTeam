using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.CreateAssetCategory;

internal sealed class CreateAssetCategoryHandler(AssetsDbContext db, IClock clock)
{
    public async Task<Result<CreateAssetCategoryResponse>> HandleAsync(
        CreateAssetCategoryRequest request,
        CancellationToken cancellationToken)
    {
        // Was previously missing entirely — an asset category name had no uniqueness check at all
        // (case-sensitive or otherwise). Added here case-insensitively, matching every other
        // "Name must be unique per company" entity in this codebase.
        var nameExists = await db.AssetCategories.AnyAsync(
            c => c.CompanyId == request.CompanyId && c.Name.ToLower() == request.Name.Trim().ToLower(),
            cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateAssetCategoryResponse>(
                Error.Conflict($"An asset category named '{request.Name.Trim()}' already exists."));
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = AssetCategory.Create(Guid.NewGuid(), request.CompanyId, request.Name, request.Description, now);

        db.AssetCategories.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateAssetCategoryResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.Description,
            entity.IsActive, entity.CreatedAt, entity.UpdatedAt));
    }
}
