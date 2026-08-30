using FluentValidation;

namespace HR.Modules.Identity.Features.ListUsers;

internal sealed class ListUsersValidator : AbstractValidator<ListUsersRequest>
{
    public ListUsersValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Page).GreaterThanOrEqualTo(1);
        // The handler materialises the whole company roster in memory regardless of page size, so a
        // larger ceiling here just controls how many rows a single response returns. Raised from 200
        // to 2000 (ADM-01) so a company with more than 200 users can be paged accurately by the
        // client without silently dropping everyone past row 200.
        RuleFor(r => r.PageSize).InclusiveBetween(1, 2000);
    }
}
