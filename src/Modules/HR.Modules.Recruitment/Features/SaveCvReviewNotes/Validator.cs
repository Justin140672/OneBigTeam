using FluentValidation;

namespace HR.Modules.Recruitment.Features.SaveCvReviewNotes;

internal sealed class SaveCvReviewNotesValidator : AbstractValidator<SaveCvReviewNotesRequest>
{
    public SaveCvReviewNotesValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.VacancyId).NotEmpty();
        RuleFor(r => r.ApplicationId).NotEmpty();

        RuleFor(r => r.CvReviewNotes)
            .MaximumLength(4000)
            .When(r => !string.IsNullOrWhiteSpace(r.CvReviewNotes));
    }
}
