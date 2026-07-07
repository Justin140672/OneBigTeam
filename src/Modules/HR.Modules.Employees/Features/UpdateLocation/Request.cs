namespace HR.Modules.Employees.Features.UpdateLocation;

internal sealed record UpdateLocationRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid LocationTypeId { get; init; }
}
