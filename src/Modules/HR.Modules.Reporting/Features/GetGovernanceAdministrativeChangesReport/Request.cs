namespace HR.Modules.Reporting.Features.GetGovernanceAdministrativeChangesReport;

/// <summary>
/// ADM-08 Administrative Changes governance report — configuration, settings, role and policy
/// changes from the central audit source.
/// </summary>
internal sealed record GetGovernanceAdministrativeChangesReportRequest(
    Guid CompanyId,
    Guid? ActorUserId = null,
    string? EventType = null,
    Guid? EmployeeId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20);
