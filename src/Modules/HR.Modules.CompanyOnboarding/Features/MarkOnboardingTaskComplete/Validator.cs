using FluentValidation;

namespace HR.Modules.CompanyOnboarding.Features.MarkOnboardingTaskComplete;

internal sealed class MarkOnboardingTaskCompleteValidator : AbstractValidator<MarkOnboardingTaskCompleteRequest>
{
    public MarkOnboardingTaskCompleteValidator()
    {
        RuleFor(x => x.TaskKey).NotEmpty().MaximumLength(200);
    }
}
