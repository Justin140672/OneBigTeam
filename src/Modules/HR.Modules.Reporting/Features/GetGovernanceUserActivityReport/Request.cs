namespace HR.Modules.Reporting.Features.GetGovernanceUserActivityReport;

/// <summary>
/// ADM-08 User Activity governance report. Every field except <see cref="CompanyId"/> is an
/// optional filter applied over the central audit source.
/// </summary>
internal sealed record GetGovernanceUserActivityReportRequest(
    Guid CompanyId,
    Guid? ActorUserId = null,
    string? EventType = null,
    Guid? EmployeeId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20);
