namespace HR.Modules.Reporting.Features.GetGovernanceSecurityEventsReport;

/// <summary>
/// ADM-08 Security Events governance report — authentication, permission, account-status and
/// role-assignment events from the central audit source.
/// </summary>
internal sealed record GetGovernanceSecurityEventsReportRequest(
    Guid CompanyId,
    Guid? ActorUserId = null,
    string? EventType = null,
    Guid? EmployeeId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20);
