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
}
