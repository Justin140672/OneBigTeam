namespace HR.Modules.Employees.Features.UpdateEmploymentType;

internal sealed record UpdateEmploymentTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset UpdatedAt);
