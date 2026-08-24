using FluentValidation;

namespace HR.Modules.Probation.Features.MarkProbationNotApplicable;

internal sealed class MarkProbationNotApplicableValidator : AbstractValidator<MarkProbationNotApplicableRequest>
{
    public MarkProbationNotApplicableValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.Reason).MaximumLength(1000).When(r => r.Reason is not null);

        // Either all three are supplied (used only to create a placeholder record when none
        // exists yet) or none are — the handler decides which case it's in based on whether a
        // record already exists, so a partial set here would be ambiguous.
        RuleFor(r => r.ManagerEmployeeId)
            .NotEmpty()
            .When(r => r.StartDate is not null || r.ExpectedEndDate is not null)
            .WithMessage("ManagerEmployeeId is required when StartDate or ExpectedEndDate is supplied.");

        RuleFor(r => r.StartDate)
            .NotEmpty()
            .When(r => r.ManagerEmployeeId is not null || r.ExpectedEndDate is not null)
            .WithMessage("StartDate is required when ManagerEmployeeId or ExpectedEndDate is supplied.");

        RuleFor(r => r.ExpectedEndDate)
            .NotEmpty()
            .When(r => r.ManagerEmployeeId is not null || r.StartDate is not null)
            .WithMessage("ExpectedEndDate is required when ManagerEmployeeId or StartDate is supplied.");
    }
}
