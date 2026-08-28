using FluentValidation;

namespace HR.Modules.Identity.Features.SearchUserAccess;

internal sealed class SearchUserAccessValidator : AbstractValidator<SearchUserAccessRequest>
{
    public SearchUserAccessValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.OverrideState).IsInEnum();
    }
}
