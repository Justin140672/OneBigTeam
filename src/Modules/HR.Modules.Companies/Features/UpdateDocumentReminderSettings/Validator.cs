using FluentValidation;

namespace HR.Modules.Companies.Features.UpdateDocumentReminderSettings;

/// <summary>
/// SET-07: values must be positive, unique and ordered (furthest-out first); at least one reminder
/// must remain configured while reminders are enabled.
/// </summary>
internal sealed class UpdateDocumentReminderSettingsValidator : AbstractValidator<UpdateDocumentReminderSettingsRequest>
{
    public UpdateDocumentReminderSettingsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Version).GreaterThan(0);

        RuleFor(r => r.OffsetDays1).GreaterThan(0).When(r => r.OffsetDays1.HasValue)
            .WithMessage("Reminder offset days must be positive.");
        RuleFor(r => r.OffsetDays2).GreaterThan(0).When(r => r.OffsetDays2.HasValue)
            .WithMessage("Reminder offset days must be positive.");
        RuleFor(r => r.OffsetDays3).GreaterThan(0).When(r => r.OffsetDays3.HasValue)
            .WithMessage("Reminder offset days must be positive.");

        RuleFor(r => r)
            .Must(r => !r.RemindersEnabled || r.OffsetDays1.HasValue || r.OffsetDays2.HasValue || r.OffsetDays3.HasValue)
            .WithMessage("At least one reminder stage must be configured while reminders are enabled.");

        RuleFor(r => r)
            .Must(HaveUniqueValues)
            .WithMessage("Reminder offset days must be unique.");

        RuleFor(r => r)
            .Must(BeStrictlyDecreasing)
            .WithMessage("Reminder offset days must be ordered furthest-out first (OffsetDays1 > OffsetDays2 > OffsetDays3).");
    }

    private static bool HaveUniqueValues(UpdateDocumentReminderSettingsRequest r)
    {
        var values = new[] { r.OffsetDays1, r.OffsetDays2, r.OffsetDays3 }
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        return values.Count == values.Distinct().Count();
    }

    private static bool BeStrictlyDecreasing(UpdateDocumentReminderSettingsRequest r)
    {
        if (r.OffsetDays1.HasValue && r.OffsetDays2.HasValue && r.OffsetDays1.Value <= r.OffsetDays2.Value)
            return false;
        if (r.OffsetDays2.HasValue && r.OffsetDays3.HasValue && r.OffsetDays2.Value <= r.OffsetDays3.Value)
            return false;
        if (r.OffsetDays1.HasValue && r.OffsetDays3.HasValue && !r.OffsetDays2.HasValue && r.OffsetDays1.Value <= r.OffsetDays3.Value)
            return false;

        return true;
    }
}
