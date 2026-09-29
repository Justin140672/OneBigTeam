using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal sealed class FakeDocumentComplianceReportReader : IDocumentComplianceReportReader
{
    private readonly IReadOnlyList<DocumentComplianceReportItem> _items;

    public FakeDocumentComplianceReportReader(IReadOnlyList<DocumentComplianceReportItem> items)
    {
        _items = items;
    }

    public Guid? LastCompanyId { get; private set; }
    public Guid? LastPositionProfileId { get; private set; }

    public Task<IReadOnlyList<DocumentComplianceReportItem>> GetDocumentComplianceReportAsync(
        Guid companyId,
        Guid? positionProfileId,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastPositionProfileId = positionProfileId;

        var result = positionProfileId is null
            ? _items
            : _items.Where(i => i.PositionProfileId == positionProfileId).ToList();

        return Task.FromResult<IReadOnlyList<DocumentComplianceReportItem>>(result);
    }
}
