using FluentValidation;

namespace HR.Modules.Leave.Features.UpdateLeaveType;

internal sealed class UpdateLeaveTypeValidator : AbstractValidator<UpdateLeaveTypeRequest>
{
    public UpdateLeaveTypeValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Code).NotEmpty().MaximumLength(20);
        RuleFor(r => r.DefaultEntitlementDays).GreaterThanOrEqualTo(0);
    }
}
