using FluentValidation;

namespace HR.Modules.Employees.Features.UpdateDepartment;

internal sealed class UpdateDepartmentValidator : AbstractValidator<UpdateDepartmentRequest>
{
    public UpdateDepartmentValidator()
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

        // Ticket 2: a loaded version is mandatory on this protected update — never last-writer-wins.
        RuleFor(r => r.ExpectedVersion)
            .NotNull()
            .WithMessage("A concurrency version is required. Reload the page and try again.");
    }
}
