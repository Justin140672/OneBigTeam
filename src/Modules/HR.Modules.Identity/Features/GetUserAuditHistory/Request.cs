namespace HR.Modules.Identity.Features.GetUserAuditHistory;

internal sealed record GetUserAuditHistoryRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
