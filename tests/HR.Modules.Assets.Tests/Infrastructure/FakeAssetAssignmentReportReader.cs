using HR.Infrastructure.Abstractions;

namespace HR.Modules.Assets.Tests.Infrastructure;

internal sealed class FakeAssetAssignmentReportReader : IAssetAssignmentReportReader
{
    private readonly IReadOnlyList<AssetAssignmentReportItem> _items;

    public FakeAssetAssignmentReportReader(IReadOnlyList<AssetAssignmentReportItem> items)
    {
        _items = items;
    }

    public Guid? LastCompanyId { get; private set; }
    public bool WasCalled { get; private set; }

    public Task<IReadOnlyList<AssetAssignmentReportItem>> GetAssetAssignmentsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        WasCalled = true;

        return Task.FromResult(_items);
    }
}
