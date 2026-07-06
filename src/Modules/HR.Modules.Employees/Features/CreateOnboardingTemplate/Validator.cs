using FluentValidation;

namespace HR.Modules.Employees.Features.CreateOnboardingTemplate;

internal sealed class CreateOnboardingTemplateValidator : AbstractValidator<CreateOnboardingTemplateRequest>
{
    public CreateOnboardingTemplateValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(2000)
            .When(r => r.Description is not null);
    }
}
