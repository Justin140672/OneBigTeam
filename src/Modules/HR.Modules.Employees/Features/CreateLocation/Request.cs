namespace HR.Modules.Employees.Features.CreateLocation;

internal sealed record CreateLocationRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid LocationTypeId { get; init; }
}
