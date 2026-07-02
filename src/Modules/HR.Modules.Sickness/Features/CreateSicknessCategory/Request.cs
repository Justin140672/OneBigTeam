namespace HR.Modules.Sickness.Features.CreateSicknessCategory;

internal sealed record CreateSicknessCategoryRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
}
