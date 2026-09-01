using FluentValidation;

namespace HR.Modules.Leave.Features.RejectLeaveRequest;

internal sealed class RejectLeaveRequestValidator : AbstractValidator<RejectLeaveRequestRequest>
{
    public RejectLeaveRequestValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LeaveRequestId).NotEmpty();
        // ReviewedByEmployeeId is NOT validated as client input: the endpoint unconditionally
        // overwrites it with the authenticated caller's id before authorization/persistence.
        // Requiring it here rejected the request before the resource-authorization check ran.
        RuleFor(r => r.RejectionReason).MaximumLength(500).When(r => r.RejectionReason is not null);
    }
}
