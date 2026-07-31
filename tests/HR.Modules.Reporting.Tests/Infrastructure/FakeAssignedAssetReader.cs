using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

// Mirrors HR.Modules.Offboarding.Tests.Infrastructure.FakeAssignedAssetReader. Returns a
// pre-configured set of assigned assets per employee, or empty (i.e. "assets returned") by
// default.
internal sealed class FakeAssignedAssetReader : IAssignedAssetReader
{
    private readonly Dictionary<Guid, IReadOnlyList<AssignedAssetItem>> _assetsByEmployee;
    private readonly IReadOnlyList<AssignedAssetItem> _defaultAssets;

    public FakeAssignedAssetReader(
        Dictionary<Guid, IReadOnlyList<AssignedAssetItem>>? assetsByEmployee = null,
        IReadOnlyList<AssignedAssetItem>? defaultAssets = null)
    {
        _assetsByEmployee = assetsByEmployee ?? new Dictionary<Guid, IReadOnlyList<AssignedAssetItem>>();
        _defaultAssets = defaultAssets ?? [];
    }

    public Task<IReadOnlyList<AssignedAssetItem>> GetAssignedAssetsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var result = _assetsByEmployee.TryGetValue(employeeId, out var assets) ? assets : _defaultAssets;
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedAssetItem>>> GetAssignedAssetsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, IReadOnlyList<AssignedAssetItem>> result = employeeIds.ToDictionary(
            id => id,
            id => _assetsByEmployee.TryGetValue(id, out var assets) ? assets : _defaultAssets);
        return Task.FromResult(result);
    }
}
