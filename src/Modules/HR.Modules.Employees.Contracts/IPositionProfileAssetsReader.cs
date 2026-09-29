namespace HR.Modules.Employees.Contracts;

public interface IPositionProfileAssetsReader
{
    Task<IReadOnlyList<PositionProfileRequiredAssetItem>> GetActiveAssetsAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken);

    Task<int> CountActiveReferencesToAssetCategoryAsync(
        Guid companyId,
        Guid assetCategoryId,
        CancellationToken cancellationToken);
}
