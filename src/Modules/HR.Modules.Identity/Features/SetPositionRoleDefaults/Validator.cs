using FluentValidation;

namespace HR.Modules.Identity.Features.SetPositionRoleDefaults;

internal sealed class SetPositionRoleDefaultsValidator : AbstractValidator<SetPositionRoleDefaultsRequest>
{
    public SetPositionRoleDefaultsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.PositionProfileId).NotEmpty();
        RuleForEach(r => r.RoleIds).NotEmpty();
    }
}
