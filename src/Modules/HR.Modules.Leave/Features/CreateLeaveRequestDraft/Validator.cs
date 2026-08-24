using FluentValidation;

namespace HR.Modules.Leave.Features.CreateLeaveRequestDraft;

// LEAVE-07: intentionally minimal - a draft only needs well-formed input. Cross-year rejection,
// balance sufficiency and conflict detection are enforced at submit time
// (SubmitLeaveRequestDraftValidator/Handler), not here. See LeaveRequestStatus.Draft doc comment.
internal sealed class CreateLeaveRequestDraftValidator : AbstractValidator<CreateLeaveRequestDraftRequest>
{
    public CreateLeaveRequestDraftValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LeaveTypeId).NotEmpty();
        RuleFor(r => r.StartDate).NotEqual(DateOnly.MinValue);
        RuleFor(r => r.EndDate).NotEqual(DateOnly.MinValue);
        RuleFor(r => r.StartPart).IsInEnum();
        RuleFor(r => r.EndPart).IsInEnum();
        RuleFor(r => r.EndDate)
            .GreaterThanOrEqualTo(r => r.StartDate)
            .WithMessage("End date must be on or after start date.")
            .When(r => r.StartDate != DateOnly.MinValue && r.EndDate != DateOnly.MinValue);
        RuleFor(r => r.Reason).MaximumLength(1000).When(r => r.Reason is not null);
    }
}
