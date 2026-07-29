using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="ICompanyDocumentAcknowledgementReportReader"/> — always returns
/// the pre-configured, company-wide item set.
/// </summary>
internal sealed class FakeCompanyDocumentAcknowledgementReportReader : ICompanyDocumentAcknowledgementReportReader
{
    private readonly IReadOnlyList<CompanyDocumentAcknowledgementReportItem> _items;

    public FakeCompanyDocumentAcknowledgementReportReader(IReadOnlyList<CompanyDocumentAcknowledgementReportItem> items)
    {
        _items = items;
    }

    public Guid? LastCompanyId { get; private set; }

    public Task<IReadOnlyList<CompanyDocumentAcknowledgementReportItem>> GetAcknowledgementReportAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;

        return Task.FromResult(_items);
    }
}
