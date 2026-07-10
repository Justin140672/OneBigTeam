using FluentValidation;

namespace HR.Modules.Employees.Features.ListOnboardingTemplatesForPositionProfile;

internal sealed class ListOnboardingTemplatesForPositionProfileValidator : AbstractValidator<ListOnboardingTemplatesForPositionProfileRequest>
{
    public ListOnboardingTemplatesForPositionProfileValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.PositionProfileId).NotEmpty();
    }
}
