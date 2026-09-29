using FluentValidation;
using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Features.CompleteReturnToWorkReview;

internal sealed class CompleteReturnToWorkReviewValidator : AbstractValidator<CompleteReturnToWorkReviewRequest>
{
    public CompleteReturnToWorkReviewValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ReviewId).NotEmpty();

        RuleFor(r => r.Outcome).IsInEnum();

        RuleFor(r => r.AdjustmentDetails)
            .NotEmpty()
            .WithMessage("Adjustment details are required when adjustments are marked as required.")
            .When(r => r.AdjustmentsRequired);

        RuleFor(r => r.AdjustmentDetails)
            .MaximumLength(2000);

        RuleFor(r => r.ManagerNotes)
            .MaximumLength(2000);
    }
}
