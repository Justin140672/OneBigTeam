using FluentValidation;

namespace HR.Modules.Identity.Features.AssignPlatformAdministratorRole;

internal sealed class AssignPlatformAdministratorRoleValidator : AbstractValidator<AssignPlatformAdministratorRoleRequest>
{
    public AssignPlatformAdministratorRoleValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Role).IsInEnum();
    }
}
