namespace HR.Modules.Companies.Features.GetCompanyAuditLog;

internal sealed record GetCompanyAuditLogRequest
{
    public Guid CompanyId { get; init; }

    /// <summary>Narrows results to events linked to a specific employee (optional).</summary>
    public Guid? EmployeeId { get; init; }

    /// <summary>Exact event type string (e.g. "employee.profile-updated") — optional.</summary>
    public string? EventType { get; init; }

    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }

    public int PageNumber { get; init; } = 1;
    public int PageSize   { get; init; } = 25;
}
