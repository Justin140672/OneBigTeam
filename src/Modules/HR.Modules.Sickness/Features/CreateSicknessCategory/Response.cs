namespace HR.Modules.Sickness.Features.CreateSicknessCategory;

internal sealed record CreateSicknessCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
