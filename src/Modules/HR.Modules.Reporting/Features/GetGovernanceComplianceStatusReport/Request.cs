namespace HR.Modules.Reporting.Features.GetGovernanceComplianceStatusReport;

/// <summary>
/// ADM-08 Compliance Status governance report. A paged, report-catalogue-surfaced view over the
/// same ADM-02 Compliance Centre data — it does not create a competing compliance record.
/// </summary>
internal sealed record GetGovernanceComplianceStatusReportRequest(
    Guid CompanyId,
    string? Category = null,
    string? Severity = null,
    string? Department = null,
    Guid? ManagerId = null,
    DateOnly? DueDateStart = null,
    DateOnly? DueDateEnd = null,
    int Page = 1,
    int PageSize = 20);
