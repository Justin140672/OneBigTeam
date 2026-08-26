using FluentValidation;

namespace HR.Modules.Identity.Features.AddEmployeeRoleOverride;

internal sealed class AddEmployeeRoleOverrideValidator : AbstractValidator<AddEmployeeRoleOverrideRequest>
{
    public AddEmployeeRoleOverrideValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.RoleId).NotEmpty();
        RuleFor(r => r.OverrideType).IsInEnum();
        RuleFor(r => r.Reason)
            .NotEmpty()
            .WithMessage("A reason is required for every permission override.")
            .MaximumLength(500);
    }
}
