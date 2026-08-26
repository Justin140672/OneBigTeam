using FluentValidation;

namespace HR.Modules.Identity.Features.ListEmployeeRoleOverrides;

internal sealed class ListEmployeeRoleOverridesValidator : AbstractValidator<ListEmployeeRoleOverridesRequest>
{
    public ListEmployeeRoleOverridesValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.UserId).NotEmpty();
    }
}
