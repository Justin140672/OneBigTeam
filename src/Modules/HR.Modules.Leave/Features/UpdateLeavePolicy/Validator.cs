using FluentValidation;

namespace HR.Modules.Leave.Features.UpdateLeavePolicy;

internal sealed class UpdateLeavePolicyValidator : AbstractValidator<UpdateLeavePolicyRequest>
{
    public UpdateLeavePolicyValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.PolicyId)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.CarryOverDays)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(365);
    }
}
