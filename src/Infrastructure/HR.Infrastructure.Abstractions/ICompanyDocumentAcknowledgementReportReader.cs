namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Company-wide acknowledgement status for every Published, RequiresAcknowledgement
/// SharedCompanyDocument, for the Company Document Acknowledgement Report (OBT-715), as owned by
/// HR.Modules.Documents. One row per (document, employee-in-audience) pair, scoped to the
/// document's current VersionNumber only — an acknowledgement recorded against an older version
/// does not count (see SharedCompanyDocumentAcknowledgement.VersionNumber doc comment).
/// </summary>
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
