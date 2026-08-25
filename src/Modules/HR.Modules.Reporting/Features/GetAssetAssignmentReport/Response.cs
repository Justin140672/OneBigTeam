namespace HR.Modules.Reporting.Features.GetAssetAssignmentReport;

internal sealed record GetAssetAssignmentReportResponse(
    IReadOnlyList<AssetAssignmentReportRow> Items,
    int TotalAssignments,
    bool IsTruncated);

internal sealed record AssetAssignmentReportRow(
    Guid EmployeeId,
    string EmployeeName,
    string AssetName,
    string? SerialNumber,
    DateTimeOffset AssignedDate,
    string ReturnStatus);
