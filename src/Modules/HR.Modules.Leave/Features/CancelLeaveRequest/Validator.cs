using FluentValidation;

namespace HR.Modules.Leave.Features.CancelLeaveRequest;

internal sealed class CancelLeaveRequestValidator : AbstractValidator<CancelLeaveRequestRequest>
{
    public CancelLeaveRequestValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LeaveRequestId).NotEmpty();
    }
}
