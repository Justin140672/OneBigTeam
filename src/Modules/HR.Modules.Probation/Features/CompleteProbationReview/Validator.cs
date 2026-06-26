using FluentValidation;
using HR.Modules.Probation.Domain;

namespace HR.Modules.Probation.Features.CompleteProbationReview;

internal sealed class CompleteProbationReviewValidator : AbstractValidator<CompleteProbationReviewRequest>
{
    public CompleteProbationReviewValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ProbationRecordId).NotEmpty();
        RuleFor(r => r.ReviewId).NotEmpty();
        RuleFor(r => r.CompletedByEmployeeId).NotEmpty();

        RuleFor(r => r.DecisionDate)
            .NotNull()
            .When(r => r.Outcome.HasValue)
            .WithMessage("DecisionDate is required when an outcome is provided.");

        RuleFor(r => r.NewExpectedEndDate)
            .NotNull()
            .When(r => r.Outcome == ProbationOutcome.Extend)
            .WithMessage("NewExpectedEndDate is required when extending probation.");

        RuleFor(r => r.NewExpectedEndDate)
            .Must(date => date > DateOnly.FromDateTime(DateTime.UtcNow))
            .When(r => r.Outcome == ProbationOutcome.Extend && r.NewExpectedEndDate.HasValue)
            .WithMessage("NewExpectedEndDate must be in the future.");

        RuleFor(r => r.ExtensionReason)
            .NotEmpty()
            .When(r => r.Outcome == ProbationOutcome.Extend)
            .WithMessage("ExtensionReason is required when extending probation.");

        RuleFor(r => r.ExtensionReason)
            .MaximumLength(1000)
            .When(r => r.ExtensionReason is not null);
    }
}
