using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Tests.Infrastructure;

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
