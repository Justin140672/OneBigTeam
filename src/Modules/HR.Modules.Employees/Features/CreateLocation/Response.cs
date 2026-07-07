namespace HR.Modules.Employees.Features.CreateLocation;

internal sealed record CreateLocationResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid LocationTypeId,
    bool IsActive,
    DateTimeOffset CreatedAt);
