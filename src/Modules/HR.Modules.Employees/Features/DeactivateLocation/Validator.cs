using FluentValidation;

namespace HR.Modules.Employees.Features.DeactivateLocation;

internal sealed class DeactivateLocationValidator : AbstractValidator<DeactivateLocationRequest>
{
    public DeactivateLocationValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
    }
}
