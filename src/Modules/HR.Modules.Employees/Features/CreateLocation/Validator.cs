using FluentValidation;

namespace HR.Modules.Employees.Features.CreateLocation;

internal sealed class CreateLocationValidator : AbstractValidator<CreateLocationRequest>
{
    public CreateLocationValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(1000)
            .When(r => r.Description is not null);

        RuleFor(r => r.LocationTypeId)
            .NotEmpty();
    }
}
