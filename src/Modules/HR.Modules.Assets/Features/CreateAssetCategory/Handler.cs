using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.SharedKernel;

namespace HR.Modules.Assets.Features.CreateAssetCategory;

internal sealed class CreateAssetCategoryHandler(AssetsDbContext db, IClock clock)
{
    public async Task<Result<CreateAssetCategoryResponse>> HandleAsync(
        CreateAssetCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = AssetCategory.Create(Guid.NewGuid(), request.CompanyId, request.Name, request.Description, now);

        db.AssetCategories.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateAssetCategoryResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.Description,
            entity.IsActive, entity.CreatedAt, entity.UpdatedAt));
    }
}
