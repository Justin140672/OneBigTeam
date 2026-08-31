using HR.Modules.Reporting.GovernanceReporting;

namespace HR.Modules.Reporting.Features.GetGovernanceSecurityEventsReport;

internal sealed record GetGovernanceSecurityEventsReportResponse(
    IReadOnlyList<GovernanceAuditRow> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool IsTruncated);
