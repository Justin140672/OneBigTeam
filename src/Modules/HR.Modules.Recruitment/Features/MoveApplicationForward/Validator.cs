using FluentValidation;

namespace HR.Modules.Recruitment.Features.MoveApplicationForward;

internal sealed class MoveApplicationForwardValidator : AbstractValidator<MoveApplicationForwardRequest>
{
    public MoveApplicationForwardValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.VacancyId).NotEmpty();
        RuleFor(r => r.ApplicationId).NotEmpty();

        RuleFor(r => r.CvReviewNotes)
            .MaximumLength(4000)
            .When(r => !string.IsNullOrWhiteSpace(r.CvReviewNotes));
    }
}
