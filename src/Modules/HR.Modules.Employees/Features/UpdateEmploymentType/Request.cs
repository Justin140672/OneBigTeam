namespace HR.Modules.Employees.Features.UpdateEmploymentType;

internal sealed record UpdateEmploymentTypeRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    // Ticket 2 (optimistic concurrency) — required; validator rejects a null/missing value.
    public int? ExpectedVersion { get; init; }
}
