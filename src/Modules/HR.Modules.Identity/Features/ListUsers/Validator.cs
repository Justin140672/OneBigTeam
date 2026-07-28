using FluentValidation;

namespace HR.Modules.Identity.Features.ListUsers;

internal sealed class ListUsersValidator : AbstractValidator<ListUsersRequest>
{
    public ListUsersValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Page).GreaterThanOrEqualTo(1);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 200);
    }
}
