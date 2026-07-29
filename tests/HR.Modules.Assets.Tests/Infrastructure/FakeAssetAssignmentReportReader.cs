using HR.Infrastructure.Abstractions;

namespace HR.Modules.Assets.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IAssetAssignmentReportReader"/> — always returns the
/// pre-configured, company-wide item set. This category is HR-only (no row-level manager scoping).
/// </summary>
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
