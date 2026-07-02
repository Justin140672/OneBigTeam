namespace HR.Modules.Sickness.Features.UpdateSicknessCategory;

internal sealed record UpdateSicknessCategoryRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
}
