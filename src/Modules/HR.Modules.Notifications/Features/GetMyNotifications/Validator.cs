using FluentValidation;

namespace HR.Modules.Notifications.Features.GetMyNotifications;

internal sealed class GetMyNotificationsValidator : AbstractValidator<GetMyNotificationsRequest>
{
    public GetMyNotificationsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);

        RuleFor(r => r.CreatedTo)
            .GreaterThanOrEqualTo(r => r.CreatedFrom!.Value)
            .When(r => r.CreatedFrom is not null && r.CreatedTo is not null)
            .WithMessage("CreatedTo must not be earlier than CreatedFrom.");
    }
}
