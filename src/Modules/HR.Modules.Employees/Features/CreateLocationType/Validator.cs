using FluentValidation;

namespace HR.Modules.Employees.Features.CreateLocationType;

internal sealed class CreateLocationTypeValidator : AbstractValidator<CreateLocationTypeRequest>
{
    public CreateLocationTypeValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(500).When(r => r.Description is not null);
    }
}
