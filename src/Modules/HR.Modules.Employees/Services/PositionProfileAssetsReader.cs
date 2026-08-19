using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class PositionProfileAssetsReader(EmployeesDbContext dbContext)
    : IPositionProfileAssetsReader
{
    public async Task<IReadOnlyList<PositionProfileRequiredAssetItem>> GetActiveAssetsAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken)
    {
        return await dbContext.PositionProfileRequiredAssets
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId
                     && a.PositionProfileId == positionProfileId
                     && a.IsActive)
            .Select(a => new PositionProfileRequiredAssetItem(
                a.Id,
                a.AssetCategoryId,
                a.IsMandatory,
                a.Quantity))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountActiveReferencesToAssetCategoryAsync(
        Guid companyId,
        Guid assetCategoryId,
        CancellationToken cancellationToken)
    {
        return dbContext.PositionProfileRequiredAssets
            .AsNoTracking()
            .CountAsync(
                a => a.CompanyId == companyId
                  && a.AssetCategoryId == assetCategoryId
                  && a.IsActive,
                cancellationToken);
    }
}
