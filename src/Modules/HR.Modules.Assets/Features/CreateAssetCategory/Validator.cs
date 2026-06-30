using FluentValidation;

namespace HR.Modules.Assets.Features.CreateAssetCategory;

internal sealed class CreateAssetCategoryValidator : AbstractValidator<CreateAssetCategoryRequest>
{
    public CreateAssetCategoryValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(500);
    }
}
