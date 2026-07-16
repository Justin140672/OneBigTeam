using FluentValidation;

namespace HR.Modules.Documents.Features.ListSharedCompanyDocumentsDueForReview;

internal sealed class ListSharedCompanyDocumentsDueForReviewValidator : AbstractValidator<ListSharedCompanyDocumentsDueForReviewRequest>
{
    public ListSharedCompanyDocumentsDueForReviewValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
