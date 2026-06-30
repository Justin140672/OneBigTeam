using FluentValidation;

namespace HR.Modules.Employees.Features.ListEmploymentTypes;

internal sealed class ListEmploymentTypesValidator : AbstractValidator<ListEmploymentTypesRequest>
{
    public ListEmploymentTypesValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
