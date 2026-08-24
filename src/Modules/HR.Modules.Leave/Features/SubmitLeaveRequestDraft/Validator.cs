using FluentValidation;

namespace HR.Modules.Leave.Features.SubmitLeaveRequestDraft;

internal sealed class SubmitLeaveRequestDraftValidator : AbstractValidator<SubmitLeaveRequestDraftRequest>
{
    public SubmitLeaveRequestDraftValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LeaveRequestId).NotEmpty();
    }
}
