using FluentValidation;

namespace HR.Modules.Leave.Features.RejectLeaveRequest;

internal sealed class RejectLeaveRequestValidator : AbstractValidator<RejectLeaveRequestRequest>
{
    public RejectLeaveRequestValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LeaveRequestId).NotEmpty();
        RuleFor(r => r.ReviewedByEmployeeId).NotEmpty();
        RuleFor(r => r.RejectionReason).MaximumLength(500).When(r => r.RejectionReason is not null);
    }
}
