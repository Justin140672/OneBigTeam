namespace HR.Modules.Sickness.Features.ListSicknessCategories;

internal sealed record ListSicknessCategoriesResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
