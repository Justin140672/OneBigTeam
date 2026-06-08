using FluentValidation;

namespace HR.Modules.Employees.Features.AssignManager;

internal sealed class AssignManagerValidator : AbstractValidator<AssignManagerRequest>
{
    public AssignManagerValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();

        RuleFor(r => r.ManagerId)
            .NotEqual(r => r.Id)
            .When(r => r.ManagerId is not null)
            .WithMessage("An employee cannot be their own manager.");
    }
}
