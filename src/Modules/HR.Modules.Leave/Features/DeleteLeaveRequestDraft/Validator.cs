using FluentValidation;

namespace HR.Modules.Leave.Features.DeleteLeaveRequestDraft;

internal sealed class DeleteLeaveRequestDraftValidator : AbstractValidator<DeleteLeaveRequestDraftRequest>
{
    public DeleteLeaveRequestDraftValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LeaveRequestId).NotEmpty();
    }
}
