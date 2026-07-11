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
}
