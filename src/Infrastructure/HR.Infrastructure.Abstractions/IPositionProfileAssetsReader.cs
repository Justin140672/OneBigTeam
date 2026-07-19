namespace HR.Infrastructure.Abstractions;

public interface IPositionProfileAssetsReader
{
    Task<IReadOnlyList<PositionProfileRequiredAssetItem>> GetActiveAssetsAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the number of active PositionProfileRequiredAsset rows (across all position profiles
    /// in the company) that reference the given AssetCategoryId. Used by
    /// Assets.DeactivateAssetCategory to block deactivation of an asset category that is still
    /// required by a position profile, without a direct module-to-module reference or database join.
    /// </summary>
    Task<int> CountActiveReferencesToAssetCategoryAsync(
        Guid companyId,
        Guid assetCategoryId,
        CancellationToken cancellationToken);
}
