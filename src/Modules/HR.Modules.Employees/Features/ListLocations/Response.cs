namespace HR.Modules.Employees.Features.ListLocations;

internal sealed record ListLocationsResponse(IReadOnlyList<LocationListItem> Items);

internal sealed record LocationListItem(
    Guid Id,
    string Name,
    Guid LocationTypeId,
    bool IsActive);
