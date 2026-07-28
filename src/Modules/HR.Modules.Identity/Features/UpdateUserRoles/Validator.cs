using FluentValidation;

namespace HR.Modules.Identity.Features.UpdateUserRoles;

internal sealed class UpdateUserRolesValidator : AbstractValidator<UpdateUserRolesRequest>
{
    public UpdateUserRolesValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.RoleIds)
            .NotEmpty()
            .WithMessage("A user must always retain at least one role.");
    }
}
