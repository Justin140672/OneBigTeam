using FluentValidation;

namespace HR.Modules.Recruitment.Features.ListCandidates;

internal sealed class ListCandidatesValidator : AbstractValidator<ListCandidatesRequest>
{
    public ListCandidatesValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 100);
    }
}
