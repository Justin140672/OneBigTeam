namespace HR.Modules.Sickness.Features.ListSicknessCategories;

internal sealed record ListSicknessCategoriesResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Ticket 2: the edit screen has no dedicated GetById endpoint — it hydrates from this list, so
    // the optimistic-concurrency token must travel with each list item or every save loads at
    // version 0 and fails the concurrency check.
    int Version);
