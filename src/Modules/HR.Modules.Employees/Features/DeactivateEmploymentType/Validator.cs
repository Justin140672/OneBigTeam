using FluentValidation;

namespace HR.Modules.Employees.Features.DeactivateEmploymentType;

internal sealed class DeactivateEmploymentTypeValidator : AbstractValidator<DeactivateEmploymentTypeRequest>
{
    public DeactivateEmploymentTypeValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
    }
}
