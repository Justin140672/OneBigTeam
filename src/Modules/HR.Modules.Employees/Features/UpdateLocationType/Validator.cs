using FluentValidation;

namespace HR.Modules.Employees.Features.UpdateLocationType;

internal sealed class UpdateLocationTypeValidator : AbstractValidator<UpdateLocationTypeRequest>
{
    public UpdateLocationTypeValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(500).When(r => r.Description is not null);
    }
}
