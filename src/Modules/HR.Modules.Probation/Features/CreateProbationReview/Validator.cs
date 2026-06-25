using FluentValidation;
using HR.Modules.Probation.Domain;

namespace HR.Modules.Probation.Features.CreateProbationReview;

internal sealed class CreateProbationReviewValidator : AbstractValidator<CreateProbationReviewRequest>
{
    public CreateProbationReviewValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ProbationRecordId).NotEmpty();
        RuleFor(r => r.ReviewType)
            .NotEmpty()
            .Must(t => Enum.TryParse<ProbationReviewType>(t, ignoreCase: true, out _))
            .WithMessage("ReviewType must be a valid review type.");
        RuleFor(r => r.DueDate).NotEmpty();
    }
}
