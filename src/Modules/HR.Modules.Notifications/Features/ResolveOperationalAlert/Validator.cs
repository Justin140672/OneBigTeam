using FluentValidation;

namespace HR.Modules.Notifications.Features.ResolveOperationalAlert;

internal sealed class ResolveOperationalAlertValidator : AbstractValidator<ResolveOperationalAlertRequest>
{
    public ResolveOperationalAlertValidator()
    {
        RuleFor(r => r.AlertId).NotEmpty();

        RuleFor(r => r.ResolutionNote)
            .NotEmpty()
            .Must(note => note is not null && note.Trim().Length is >= 5 and <= 1000)
            .WithMessage("Resolution note must be between 5 and 1000 characters.");
    }
}
