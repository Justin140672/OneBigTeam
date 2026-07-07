namespace HR.Modules.Employees.Features.CreateLocationType;

internal sealed record CreateLocationTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
