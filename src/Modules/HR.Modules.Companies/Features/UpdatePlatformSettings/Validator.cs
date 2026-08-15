using FluentValidation;

namespace HR.Modules.Companies.Features.UpdatePlatformSettings;

internal sealed class UpdatePlatformSettingsValidator : AbstractValidator<UpdatePlatformSettingsRequest>
{
    public UpdatePlatformSettingsValidator()
    {
        RuleFor(r => r.TrialLengthDays)
            .GreaterThan(0);

        RuleFor(r => r.DefaultMonthlyPriceGbp)
            .GreaterThanOrEqualTo(0);

        RuleFor(r => r.SupportEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);

        RuleFor(r => r.MaintenanceModeMessage)
            .MaximumLength(2000)
            .When(r => !string.IsNullOrWhiteSpace(r.MaintenanceModeMessage));
    }
}
