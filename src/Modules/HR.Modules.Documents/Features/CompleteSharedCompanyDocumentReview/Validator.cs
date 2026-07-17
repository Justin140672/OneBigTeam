using FluentValidation;

namespace HR.Modules.Documents.Features.CompleteSharedCompanyDocumentReview;

internal sealed class CompleteSharedCompanyDocumentReviewValidator : AbstractValidator<CompleteSharedCompanyDocumentReviewRequest>
{
    public CompleteSharedCompanyDocumentReviewValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.DocumentId).NotEmpty();

        RuleFor(r => r.ReviewNotes)
            .NotEmpty()
            .MaximumLength(2000);
    }
}
