using FluentValidation;

namespace HR.Modules.Employees.Features.AddOnboardingTemplateToPositionProfile;

internal sealed class AddOnboardingTemplateValidator : AbstractValidator<AddOnboardingTemplateRequest>
{
    public AddOnboardingTemplateValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.PositionProfileId).NotEmpty();
        RuleFor(r => r.OnboardingTemplateId).NotEmpty();
    }
}
