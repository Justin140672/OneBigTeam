namespace HR.Modules.Employees.Features.UpdateDepartment;

internal sealed record UpdateDepartmentRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public Guid? ManagerEmployeeId { get; init; }

    // Ticket 2 (optimistic concurrency) — the version the client loaded. Required: the validator
    // rejects a null/missing value so a stale edit can never silently overwrite a newer one.
    public int? ExpectedVersion { get; init; }
}
