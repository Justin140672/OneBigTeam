namespace HR.Infrastructure.Abstractions;

public interface IAssetCategoryReader
{
    Task<bool> ExistsAsync(Guid companyId, Guid assetCategoryId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId,
        IEnumerable<Guid> assetCategoryIds,
        CancellationToken cancellationToken);
}
