using HR.Modules.Assets.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Services;

internal sealed class AssetCategoryReader(AssetsDbContext dbContext) : IAssetCategoryReader
{
    public Task<bool> ExistsAsync(Guid companyId, Guid assetCategoryId, CancellationToken cancellationToken)
        => dbContext.AssetCategories.AnyAsync(
            c => c.Id == assetCategoryId && c.CompanyId == companyId && c.IsActive,
            cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId,
        IEnumerable<Guid> assetCategoryIds,
        CancellationToken cancellationToken)
    {
        var ids = assetCategoryIds.ToList();
        return await dbContext.AssetCategories
            .Where(c => c.CompanyId == companyId && ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
    }
}
