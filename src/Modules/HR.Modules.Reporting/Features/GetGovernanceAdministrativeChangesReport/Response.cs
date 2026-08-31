using HR.Modules.Reporting.GovernanceReporting;

namespace HR.Modules.Reporting.Features.GetGovernanceAdministrativeChangesReport;

internal sealed record GetGovernanceAdministrativeChangesReportResponse(
    IReadOnlyList<GovernanceAuditRow> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool IsTruncated);
