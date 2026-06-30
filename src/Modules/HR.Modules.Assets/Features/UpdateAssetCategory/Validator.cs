using FluentValidation;

namespace HR.Modules.Assets.Features.UpdateAssetCategory;

internal sealed class UpdateAssetCategoryValidator : AbstractValidator<UpdateAssetCategoryRequest>
{
    public UpdateAssetCategoryValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(500);
    }
}
