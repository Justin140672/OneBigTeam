using FluentValidation;

namespace HR.Modules.Employees.Features.CreateDepartment;

internal sealed class CreateDepartmentValidator : AbstractValidator<CreateDepartmentRequest>
{
    public CreateDepartmentValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(1000)
            .When(r => r.Description is not null);
    }
}
