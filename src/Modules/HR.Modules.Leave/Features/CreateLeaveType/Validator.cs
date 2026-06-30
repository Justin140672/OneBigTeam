using FluentValidation;

namespace HR.Modules.Leave.Features.CreateLeaveType;

internal sealed class CreateLeaveTypeValidator : AbstractValidator<CreateLeaveTypeRequest>
{
    public CreateLeaveTypeValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Code).NotEmpty().MaximumLength(20);
        RuleFor(r => r.DefaultEntitlementDays).GreaterThanOrEqualTo(0);
    }
}
