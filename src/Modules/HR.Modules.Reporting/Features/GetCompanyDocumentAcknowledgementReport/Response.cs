namespace HR.Modules.Reporting.Features.GetCompanyDocumentAcknowledgementReport;

internal sealed record GetCompanyDocumentAcknowledgementReportResponse(
    IReadOnlyList<CompanyDocumentAcknowledgementReportRow> Items,
    int TotalRequired,
    int TotalAcknowledged,
    int TotalOutstanding);

internal sealed record CompanyDocumentAcknowledgementReportRow(
    string DocumentTitle,
    Guid EmployeeId,
    string EmployeeName,
    bool Acknowledged,
    DateTimeOffset? AcknowledgedAt);
