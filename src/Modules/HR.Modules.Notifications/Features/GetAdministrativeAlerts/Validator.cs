using FluentValidation;

namespace HR.Modules.Notifications.Features.GetAdministrativeAlerts;

internal sealed class GetAdministrativeAlertsValidator : AbstractValidator<GetAdministrativeAlertsRequest>
{
    public GetAdministrativeAlertsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);

        RuleFor(r => r.OccurredTo)
            .GreaterThanOrEqualTo(r => r.OccurredFrom!.Value)
            .When(r => r.OccurredFrom is not null && r.OccurredTo is not null)
            .WithMessage("OccurredTo must not be earlier than OccurredFrom.");
    }
}
