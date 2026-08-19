using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Assets.Tests.Infrastructure;

internal sealed class FakePositionProfileAssetsReader(
    IReadOnlyList<PositionProfileRequiredAssetItem> items) : IPositionProfileAssetsReader
{
    public Task<IReadOnlyList<PositionProfileRequiredAssetItem>> GetActiveAssetsAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken)
        => Task.FromResult(items);

    public Task<int> CountActiveReferencesToAssetCategoryAsync(
        Guid companyId,
        Guid assetCategoryId,
        CancellationToken cancellationToken)
        => Task.FromResult(items.Count(i => i.AssetCategoryId == assetCategoryId));
}
