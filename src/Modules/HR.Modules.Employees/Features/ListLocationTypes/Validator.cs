using FluentValidation;

namespace HR.Modules.Employees.Features.ListLocationTypes;

internal sealed class ListLocationTypesValidator : AbstractValidator<ListLocationTypesRequest>
{
    public ListLocationTypesValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
