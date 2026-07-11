using FluentValidation;

namespace HR.Modules.Offboarding.Features.StartOffboarding;

internal sealed class StartOffboardingValidator : AbstractValidator<StartOffboardingRequest>
{
    public StartOffboardingValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LastWorkingDay).NotEqual(default(DateOnly));
        RuleFor(r => r.Notes).MaximumLength(2000);
    }
}
