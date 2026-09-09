using FluentValidation;

namespace HR.Modules.Employees.Features.UpdateEmploymentType;

internal sealed class UpdateEmploymentTypeValidator : AbstractValidator<UpdateEmploymentTypeRequest>
{
    public UpdateEmploymentTypeValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(500).When(r => r.Description is not null);

        // Ticket 2: a loaded version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion)
            .NotNull()
            .WithMessage("A concurrency version is required. Reload the page and try again.");
    }
}
