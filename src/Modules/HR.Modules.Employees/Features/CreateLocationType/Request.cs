namespace HR.Modules.Employees.Features.CreateLocationType;

internal sealed record CreateLocationTypeRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
