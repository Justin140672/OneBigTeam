using FluentValidation;

namespace HR.Modules.Probation.Features.CompleteProbationReview;

internal sealed class CompleteProbationReviewValidator : AbstractValidator<CompleteProbationReviewRequest>
{
    public CompleteProbationReviewValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ProbationRecordId).NotEmpty();
        RuleFor(r => r.ReviewId).NotEmpty();
        RuleFor(r => r.CompletedByEmployeeId).NotEmpty();
    }
}
