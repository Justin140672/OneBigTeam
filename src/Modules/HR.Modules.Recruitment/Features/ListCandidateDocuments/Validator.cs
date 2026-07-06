using FluentValidation;

namespace HR.Modules.Recruitment.Features.ListCandidateDocuments;

internal sealed class ListCandidateDocumentsValidator : AbstractValidator<ListCandidateDocumentsRequest>
{
    public ListCandidateDocumentsValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.CandidateId)
            .NotEmpty();
    }
}
