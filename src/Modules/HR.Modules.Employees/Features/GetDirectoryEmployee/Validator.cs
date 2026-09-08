using FluentValidation;

namespace HR.Modules.Employees.Features.GetDirectoryEmployee;

internal sealed class GetDirectoryEmployeeValidator : AbstractValidator<GetDirectoryEmployeeRequest>
{
    public GetDirectoryEmployeeValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Id)
            .NotEmpty();
    }
}
