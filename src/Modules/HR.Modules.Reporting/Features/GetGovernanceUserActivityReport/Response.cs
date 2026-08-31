using HR.Modules.Reporting.GovernanceReporting;

namespace HR.Modules.Reporting.Features.GetGovernanceUserActivityReport;

internal sealed record GetGovernanceUserActivityReportResponse(
    IReadOnlyList<GovernanceAuditRow> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool IsTruncated);
