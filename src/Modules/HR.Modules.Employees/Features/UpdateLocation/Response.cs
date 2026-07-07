namespace HR.Modules.Employees.Features.UpdateLocation;

internal sealed record UpdateLocationResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid LocationTypeId,
    bool IsActive,
    DateTimeOffset UpdatedAt);
