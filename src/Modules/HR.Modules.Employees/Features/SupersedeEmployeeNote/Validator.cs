using FluentValidation;

namespace HR.Modules.Employees.Features.SupersedeEmployeeNote;

internal sealed class SupersedeEmployeeNoteValidator : AbstractValidator<SupersedeEmployeeNoteRequest>
{
    public SupersedeEmployeeNoteValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.EmployeeId)
            .NotEmpty();

        RuleFor(r => r.OriginalNoteId)
            .NotEmpty();

        RuleFor(r => r.Category)
            .IsInEnum();

        RuleFor(r => r.NoteText)
            .Must(t => !string.IsNullOrWhiteSpace(t))
            .WithMessage("NoteText is required.")
            .MaximumLength(4000);
    }
}
