namespace HR.Modules.Employees.Features.ListLocations;

internal sealed record ListLocationsRequest
{
    public Guid CompanyId { get; init; }
    public bool IncludeInactive { get; init; } = false;
}
