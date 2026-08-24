using FluentValidation;
using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Features.CompleteReturnToWorkReview;

internal sealed class CompleteReturnToWorkReviewValidator : AbstractValidator<CompleteReturnToWorkReviewRequest>
{
    public CompleteReturnToWorkReviewValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ReviewId).NotEmpty();

        // AC: "a review cannot be completed without a fit-to-return outcome". Outcome is a
        // non-nullable enum on the request, so a missing value from the client model-binds to
        // the enum's default (Fit, 0) rather than null — IsInEnum still rejects any out-of-range
        // integer a client sends, and the field is otherwise required by construction.
        RuleFor(r => r.Outcome).IsInEnum();

        // AC: "required-adjustment information is captured when adjustments are selected".
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
