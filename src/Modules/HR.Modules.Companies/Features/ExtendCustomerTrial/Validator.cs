using FluentValidation;

namespace HR.Modules.Companies.Features.ExtendCustomerTrial;

internal sealed class ExtendCustomerTrialValidator : AbstractValidator<ExtendCustomerTrialRequest>
{
    public ExtendCustomerTrialValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.NewTrialExpiresAt)
            .NotEqual(default(DateTimeOffset));

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(1000);
    }
}
