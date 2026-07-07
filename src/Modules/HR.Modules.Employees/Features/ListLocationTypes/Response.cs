namespace HR.Modules.Employees.Features.ListLocationTypes;

internal sealed record ListLocationTypesResponse(IReadOnlyList<LocationTypeItem> Items);

internal sealed record LocationTypeItem(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
