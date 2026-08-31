namespace HR.Modules.Reporting.Features.GetGovernanceComplianceStatusReport;

internal sealed record GetGovernanceComplianceStatusReportResponse(
    IReadOnlyList<ComplianceStatusRow> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool IsTruncated);

internal sealed record ComplianceStatusRow(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    string Category,
    string CategoryLabel,
    string Detail,
    DateOnly? DueDate,
    string Severity);
