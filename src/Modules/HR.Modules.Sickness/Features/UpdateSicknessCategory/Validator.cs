using FluentValidation;

namespace HR.Modules.Sickness.Features.UpdateSicknessCategory;

internal sealed class UpdateSicknessCategoryValidator : AbstractValidator<UpdateSicknessCategoryRequest>
{
    public UpdateSicknessCategoryValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}
