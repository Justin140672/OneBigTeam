namespace HR.Modules.Sickness.Features.UpdateSicknessCategory;

internal sealed record UpdateSicknessCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
