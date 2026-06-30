using FluentValidation;

namespace HR.Modules.Employees.Features.ListEmployees;

internal sealed class ListEmployeesValidator : AbstractValidator<ListEmployeesRequest>
{
    public ListEmployeesValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 500);

        RuleFor(r => r.Search)
            .MaximumLength(200)
            .When(r => r.Search is not null);
    }
}
