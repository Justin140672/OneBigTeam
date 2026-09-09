using System;

using FluentValidation;

using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Features.ListOperationalAlerts;

internal sealed class ListOperationalAlertsValidator : AbstractValidator<ListOperationalAlertsRequest>
{
    public ListOperationalAlertsValidator()
    {
        RuleFor(r => r.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(r => r.Category)
            .Must(category => Enum.TryParse<AdministrativeAlertCategory>(category, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed)
                && !int.TryParse(category, out _))
            .When(r => r.Category is not null)
            .WithMessage("Category must be a valid administrative alert category.");

        RuleFor(r => r.Status)
            .Must(status => status is "open" or "resolved")
            .When(r => r.Status is not null)
            .WithMessage("Status must be 'open' or 'resolved'.");
    }
}
