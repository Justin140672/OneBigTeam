using FluentValidation;

namespace HR.Modules.Notifications.Features.ResolveAdministrativeAlert;

internal sealed class ResolveAdministrativeAlertValidator : AbstractValidator<ResolveAdministrativeAlertRequest>
{
    public ResolveAdministrativeAlertValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ResolutionNote).MaximumLength(1000);
    }
}
