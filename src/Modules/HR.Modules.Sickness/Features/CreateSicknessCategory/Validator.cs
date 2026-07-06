using FluentValidation;

namespace HR.Modules.Sickness.Features.CreateSicknessCategory;

internal sealed class CreateSicknessCategoryValidator : AbstractValidator<CreateSicknessCategoryRequest>
{
    public CreateSicknessCategoryValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}
