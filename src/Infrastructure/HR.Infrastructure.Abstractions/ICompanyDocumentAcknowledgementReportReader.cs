namespace HR.Infrastructure.Abstractions;

public interface ICompanyDocumentAcknowledgementReportReader
{
    Task<IReadOnlyList<CompanyDocumentAcknowledgementReportItem>> GetAcknowledgementReportAsync(
        Guid companyId,
        CancellationToken cancellationToken);
}

public sealed record CompanyDocumentAcknowledgementReportItem(
    Guid SharedCompanyDocumentId,
    string DocumentTitle,
    Guid EmployeeId,
    bool Acknowledged,
    DateTimeOffset? AcknowledgedAt);
