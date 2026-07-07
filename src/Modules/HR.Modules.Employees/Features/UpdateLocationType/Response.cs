namespace HR.Modules.Employees.Features.UpdateLocationType;

internal sealed record UpdateLocationTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset UpdatedAt);
