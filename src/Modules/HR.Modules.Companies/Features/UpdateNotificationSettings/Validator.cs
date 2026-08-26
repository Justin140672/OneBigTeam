using FluentValidation;

namespace HR.Modules.Companies.Features.UpdateNotificationSettings;

internal sealed class UpdateNotificationSettingsValidator : AbstractValidator<UpdateNotificationSettingsRequest>
{
    public UpdateNotificationSettingsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Version).GreaterThan(0);
    }
}
