using FluentValidation;

namespace HR.Modules.Employees.Features.UpdateOnboardingTemplate;

internal sealed class UpdateOnboardingTemplateValidator : AbstractValidator<UpdateOnboardingTemplateRequest>
{
    public UpdateOnboardingTemplateValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Id)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(2000)
            .When(r => r.Description is not null);

        // Ticket 2: a loaded version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion)
            .NotNull()
            .WithMessage("A concurrency version is required. Reload the page and try again.");

        RuleForEach(r => r.Tasks).ChildRules(task =>
        {
            task.RuleFor(t => t.Title)
                .NotEmpty()
                .MaximumLength(200);

            task.RuleFor(t => t.Description)
                .MaximumLength(2000)
                .When(t => t.Description is not null);

            task.RuleFor(t => t.Priority)
                .IsInEnum();

            task.RuleFor(t => t.AssignTo)
                .IsInEnum();

            task.RuleFor(t => t.DueDaysAfterStart)
                .GreaterThanOrEqualTo(0)
                .WithMessage("DueDaysAfterStart must be 0 or greater.");

            task.RuleFor(t => t.DisplayOrder)
                .GreaterThanOrEqualTo(0);
        });
    }
}
