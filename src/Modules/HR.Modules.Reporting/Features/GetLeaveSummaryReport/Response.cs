namespace HR.Modules.Reporting.Features.GetLeaveSummaryReport;

internal sealed record GetLeaveSummaryReportResponse(
    IReadOnlyList<LeaveSummaryGroupRow> Items,
    int TotalCount,
    bool IsTruncated);

internal sealed record LeaveSummaryGroupRow(
    string GroupKey,
    string GroupLabel,
    decimal EntitlementDays,
    decimal BookedDays,
    decimal ApprovedDays,
    decimal RemainingDays,
    int PendingRequestCount);
