using FluentValidation;

namespace HR.Modules.Tasks.Features.CreateTask;

internal sealed class CreateTaskValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(2000)
            .When(r => r.Description is not null);

        RuleFor(r => r.Priority)
            .IsInEnum();

        RuleFor(r => r.Source)
            .IsInEnum();
    }
}
