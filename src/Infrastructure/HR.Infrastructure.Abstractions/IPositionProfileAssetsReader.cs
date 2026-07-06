namespace HR.Infrastructure.Abstractions;

public interface IPositionProfileAssetsReader
{
    Task<IReadOnlyList<PositionProfileRequiredAssetItem>> GetActiveAssetsAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken);
}
