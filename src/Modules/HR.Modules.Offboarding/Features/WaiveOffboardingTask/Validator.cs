using FluentValidation;

namespace HR.Modules.Offboarding.Features.WaiveOffboardingTask;

internal sealed class WaiveOffboardingTaskValidator : AbstractValidator<WaiveOffboardingTaskRequest>
{
    public WaiveOffboardingTaskValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.OffboardingTaskId).NotEmpty();
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(2000);
    }
}
