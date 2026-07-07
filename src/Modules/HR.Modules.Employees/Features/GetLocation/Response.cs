namespace HR.Modules.Employees.Features.GetLocation;

internal sealed record GetLocationResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid LocationTypeId,
    bool IsActive);
