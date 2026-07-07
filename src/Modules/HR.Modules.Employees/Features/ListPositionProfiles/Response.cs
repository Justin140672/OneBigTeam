namespace HR.Modules.Employees.Features.ListPositionProfiles;

internal sealed record ListPositionProfilesResponse(IReadOnlyList<PositionProfileListItem> Items);

internal sealed record PositionProfileListItem(
    Guid Id,
    string? DepartmentName,
    string Title,
    string? Description,
    bool IsActive,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryType);
