using FluentValidation;

namespace HR.Modules.Leave.Features.DeactivateLeaveType;

internal sealed class DeactivateLeaveTypeValidator : AbstractValidator<DeactivateLeaveTypeRequest>
{
    public DeactivateLeaveTypeValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
    }
}
