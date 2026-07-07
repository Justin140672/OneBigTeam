using FluentValidation;

namespace HR.Modules.Employees.Features.DeactivateLocationType;

internal sealed class DeactivateLocationTypeValidator : AbstractValidator<DeactivateLocationTypeRequest>
{
    public DeactivateLocationTypeValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
    }
}
