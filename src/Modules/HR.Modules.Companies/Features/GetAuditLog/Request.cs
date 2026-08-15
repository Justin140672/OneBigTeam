namespace HR.Modules.Companies.Features.GetAuditLog;

/// <summary>
/// Platform Audit Log (Audit epic) filters. All filters are optional/additive — omitting all of
/// them returns the most recent platform-wide audit entries, newest first. EventType is one of the
/// explicit values in GetAuditLogResponse.AvailableEventTypes (an "All actions" UI choice maps to
/// null/omitted, same explicit-value convention as GetFailedPaymentsRequest.StatusFilter).
/// </summary>
internal sealed record GetAuditLogRequest
{
    public Guid? CompanyId { get; init; }
    public string? AdministratorEmail { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public string? EventType { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
