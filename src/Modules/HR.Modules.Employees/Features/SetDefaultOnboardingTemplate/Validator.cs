using FluentValidation;

namespace HR.Modules.Employees.Features.SetDefaultOnboardingTemplate;

internal sealed class SetDefaultOnboardingTemplateValidator : AbstractValidator<SetDefaultOnboardingTemplateRequest>
{
    public SetDefaultOnboardingTemplateValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}
