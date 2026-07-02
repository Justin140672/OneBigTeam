namespace HR.Modules.Sickness.Features.DeactivateSicknessCategory;

internal sealed record DeactivateSicknessCategoryRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
