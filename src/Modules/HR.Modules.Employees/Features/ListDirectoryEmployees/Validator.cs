using FluentValidation;

namespace HR.Modules.Employees.Features.ListDirectoryEmployees;

internal sealed class ListDirectoryEmployeesValidator : AbstractValidator<ListDirectoryEmployeesRequest>
{
    public ListDirectoryEmployeesValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(r => r.Search)
            .MaximumLength(200)
            .When(r => r.Search is not null);
    }
}
