using FluentValidation;

namespace HR.Modules.Recruitment.Features.SearchApplications;

internal sealed class SearchApplicationsValidator : AbstractValidator<SearchApplicationsRequest>
{
    public SearchApplicationsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Search).MaximumLength(200).When(r => r.Search is not null);
        RuleFor(r => r.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 200);
    }
}
