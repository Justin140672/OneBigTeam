using FluentValidation;

namespace HR.Modules.Employees.Features.UpdateLocation;

internal sealed class UpdateLocationValidator : AbstractValidator<UpdateLocationRequest>
{
    public UpdateLocationValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Id)
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
