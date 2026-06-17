using FluentValidation;

namespace HR.Modules.Leave.Features.SubmitLeaveRequest;

internal sealed class SubmitLeaveRequestValidator : AbstractValidator<SubmitLeaveRequestRequest>
{
    public SubmitLeaveRequestValidator()
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
