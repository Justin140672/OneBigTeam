using FluentValidation;

namespace HR.Modules.Leave.Features.ApproveLeaveRequest;

internal sealed class ApproveLeaveRequestValidator : AbstractValidator<ApproveLeaveRequestRequest>
{
    public ApproveLeaveRequestValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LeaveRequestId).NotEmpty();
        // ReviewedByEmployeeId is NOT validated as client input: the endpoint unconditionally
        // overwrites it with the authenticated caller's id before authorization/persistence
        // (see Endpoint.HandleAsync). Requiring it here rejected the request at the validation
        // stage — before the resource-authorization check could run — so a self-approval attempt
        // returned 422 instead of the correct 403.
    }
}
