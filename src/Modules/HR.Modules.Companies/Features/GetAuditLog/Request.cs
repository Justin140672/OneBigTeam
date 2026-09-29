namespace HR.Modules.Companies.Features.GetAuditLog;

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
