using FluentValidation;

namespace HR.Modules.Identity.Features.ListInvitableEmployees;

internal sealed class ListInvitableEmployeesValidator : AbstractValidator<ListInvitableEmployeesRequest>
{
    public ListInvitableEmployeesValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
