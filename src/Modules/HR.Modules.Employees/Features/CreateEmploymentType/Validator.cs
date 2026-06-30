using FluentValidation;

namespace HR.Modules.Employees.Features.CreateEmploymentType;

internal sealed class CreateEmploymentTypeValidator : AbstractValidator<CreateEmploymentTypeRequest>
{
    public CreateEmploymentTypeValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(500).When(r => r.Description is not null);
    }
}
