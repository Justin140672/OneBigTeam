namespace HR.Modules.Sickness.Features.ListSicknessCategories;

internal sealed record ListSicknessCategoriesRequest
{
    public Guid CompanyId { get; init; }
}
