using FluentValidation;

namespace HR.Modules.Employees.Features.SearchEmployeeDirectory;

internal sealed class SearchEmployeeDirectoryValidator : AbstractValidator<SearchEmployeeDirectoryRequest>
{
    public SearchEmployeeDirectoryValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Term)
            .MaximumLength(200)
            .When(r => r.Term is not null);

        RuleFor(r => r.Limit)
            .InclusiveBetween(1, 50);
    }
}
