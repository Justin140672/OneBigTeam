using FluentValidation;

namespace HR.Modules.Leave.Features.CreateLeavePolicy;

internal sealed class CreateLeavePolicyValidator : AbstractValidator<CreateLeavePolicyRequest>
{
    public CreateLeavePolicyValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.CarryOverDays)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(365);
    }
}
