using HR.Infrastructure.Abstractions;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeAssignedAssetReader(
    IReadOnlyList<AssignedAssetItem>? items = null) : IAssignedAssetReader
{
    private readonly IReadOnlyList<AssignedAssetItem> _items = items ?? [];

    public Task<IReadOnlyList<AssignedAssetItem>> GetAssignedAssetsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_items);

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedAssetItem>>> GetAssignedAssetsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedAssetItem>>>(
            employeeIds.ToDictionary(id => id, _ => _items));
}
