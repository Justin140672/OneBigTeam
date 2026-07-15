using FluentValidation;
using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentMetadata;

internal sealed class UpdateSharedCompanyDocumentMetadataValidator : AbstractValidator<UpdateSharedCompanyDocumentMetadataRequest>
{
    public UpdateSharedCompanyDocumentMetadataValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.DocumentId)
            .NotEmpty();

        RuleFor(r => r.CategoryId)
            .NotEmpty();

        RuleFor(r => r.CustomReviewFrequencyMonths)
            .NotNull()
            .When(r => r.ReviewFrequency == SharedCompanyDocumentReviewFrequency.Custom)
            .WithMessage("CustomReviewFrequencyMonths is required when Review Frequency is Custom.");

        RuleFor(r => r.CustomReviewFrequencyMonths)
            .GreaterThan(0)
            .When(r => r.ReviewFrequency == SharedCompanyDocumentReviewFrequency.Custom && r.CustomReviewFrequencyMonths.HasValue)
            .WithMessage("CustomReviewFrequencyMonths must be greater than zero.");

        RuleFor(r => r.ReviewDate)
            .NotNull()
            .When(r => r.ReviewFrequency != SharedCompanyDocumentReviewFrequency.None)
            .WithMessage("A next review date is required when a review frequency is set.");

        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(2000)
            .When(r => r.Description is not null);
    }
}
