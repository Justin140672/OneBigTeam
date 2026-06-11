using FluentValidation;

namespace HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;

internal sealed class AssignLeavePolicyToEmployeeValidator : AbstractValidator<AssignLeavePolicyToEmployeeRequest>
{
    public AssignLeavePolicyToEmployeeValidator()
    {
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LeavePolicyId).NotEmpty();
        RuleFor(r => r.EffectiveFrom).NotEqual(DateOnly.MinValue);
    }
}
