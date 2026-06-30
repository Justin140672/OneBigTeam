namespace HR.Modules.Employees.Features.CreateEmploymentType;

internal sealed record CreateEmploymentTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
