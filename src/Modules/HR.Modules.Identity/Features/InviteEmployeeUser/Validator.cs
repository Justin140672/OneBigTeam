using FluentValidation;

namespace HR.Modules.Identity.Features.InviteEmployeeUser;

internal sealed class InviteEmployeeUserValidator : AbstractValidator<InviteEmployeeUserRequest>
{
    public InviteEmployeeUserValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.Email).NotEmpty().EmailAddress();
        RuleFor(r => r.RoleIds).NotEmpty().WithMessage("At least one role must be selected.");
    }
}
