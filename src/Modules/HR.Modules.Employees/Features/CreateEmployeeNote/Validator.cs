using FluentValidation;

namespace HR.Modules.Employees.Features.CreateEmployeeNote;

internal sealed class CreateEmployeeNoteValidator : AbstractValidator<CreateEmployeeNoteRequest>
{
    public CreateEmployeeNoteValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.EmployeeId)
            .NotEmpty();

        RuleFor(r => r.Category)
            .IsInEnum();

        RuleFor(r => r.NoteText)
            .Must(t => !string.IsNullOrWhiteSpace(t))
            .WithMessage("NoteText is required.")
            .MaximumLength(4000);
    }
}
