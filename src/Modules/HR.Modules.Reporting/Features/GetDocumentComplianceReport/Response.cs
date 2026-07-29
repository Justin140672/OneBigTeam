namespace HR.Modules.Reporting.Features.GetDocumentComplianceReport;

internal sealed record GetDocumentComplianceReportResponse(
    IReadOnlyList<DocumentComplianceReportRow> Items,
    int TotalEmployees,
    int TotalMissing,
    int TotalExpiringSoon,
    int TotalExpired);

internal sealed record DocumentComplianceReportRow(
    Guid EmployeeId,
    string EmployeeName,
    int RequiredCount,
    int UploadedCount,
    int MissingCount,
    int ExpiringSoonCount,
    int ExpiredCount,
    IReadOnlyList<string> MissingDocumentTypeNames);
