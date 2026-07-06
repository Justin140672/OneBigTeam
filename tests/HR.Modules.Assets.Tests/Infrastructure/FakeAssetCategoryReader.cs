using HR.Infrastructure.Abstractions;

namespace HR.Modules.Assets.Tests.Infrastructure;

internal sealed class FakeAssetCategoryReader(IReadOnlyDictionary<Guid, string> names) : IAssetCategoryReader
{
    public Task<bool> ExistsAsync(Guid companyId, Guid assetCategoryId, CancellationToken cancellationToken)
        => Task.FromResult(names.ContainsKey(assetCategoryId));

    public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId,
        IEnumerable<Guid> assetCategoryIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string> result = assetCategoryIds
            .Where(names.ContainsKey)
            .ToDictionary(id => id, id => names[id]);
        return Task.FromResult(result);
    }
}
