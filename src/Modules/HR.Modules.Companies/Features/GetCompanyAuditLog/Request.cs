namespace HR.Modules.Companies.Features.GetCompanyAuditLog;

internal sealed record GetCompanyAuditLogRequest
{
    public Guid CompanyId { get; init; }

    public Guid? EmployeeId { get; init; }

    public string? EventType { get; init; }

    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }

    public int PageNumber { get; init; } = 1;
    public int PageSize   { get; init; } = 25;
}
