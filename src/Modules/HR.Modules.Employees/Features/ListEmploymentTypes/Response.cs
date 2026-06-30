namespace HR.Modules.Employees.Features.ListEmploymentTypes;

internal sealed record ListEmploymentTypesResponse(IReadOnlyList<EmploymentTypeItem> Items);

internal sealed record EmploymentTypeItem(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
