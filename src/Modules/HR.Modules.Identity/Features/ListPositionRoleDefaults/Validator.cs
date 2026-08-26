using FluentValidation;

namespace HR.Modules.Identity.Features.ListPositionRoleDefaults;

internal sealed class ListPositionRoleDefaultsValidator : AbstractValidator<ListPositionRoleDefaultsRequest>
{
    public ListPositionRoleDefaultsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
