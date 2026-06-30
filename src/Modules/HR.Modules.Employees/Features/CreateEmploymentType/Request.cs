namespace HR.Modules.Employees.Features.CreateEmploymentType;

internal sealed record CreateEmploymentTypeRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
