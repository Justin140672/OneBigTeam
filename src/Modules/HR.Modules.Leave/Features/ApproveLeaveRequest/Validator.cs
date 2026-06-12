using FluentValidation;

namespace HR.Modules.Leave.Features.ApproveLeaveRequest;

internal sealed class ApproveLeaveRequestValidator : AbstractValidator<ApproveLeaveRequestRequest>
{
    public ApproveLeaveRequestValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LeaveRequestId).NotEmpty();
        RuleFor(r => r.ReviewedByEmployeeId).NotEmpty();
    }
}
