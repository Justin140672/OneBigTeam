using FluentValidation;

namespace HR.Modules.Employees.Features.ListDepartments;

internal sealed class ListDepartmentsValidator : AbstractValidator<ListDepartmentsRequest>
{
    public ListDepartmentsValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
