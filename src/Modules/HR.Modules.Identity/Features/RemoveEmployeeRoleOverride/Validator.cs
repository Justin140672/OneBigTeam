using FluentValidation;

namespace HR.Modules.Identity.Features.RemoveEmployeeRoleOverride;

internal sealed class RemoveEmployeeRoleOverrideValidator : AbstractValidator<RemoveEmployeeRoleOverrideRequest>
{
    public RemoveEmployeeRoleOverrideValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.RoleId).NotEmpty();
    }
}
