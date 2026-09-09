using FluentValidation;

namespace HR.Modules.Notifications.Features.GetOperationalAlert;

internal sealed class GetOperationalAlertValidator : AbstractValidator<GetOperationalAlertRequest>
{
    public GetOperationalAlertValidator()
    {
        RuleFor(r => r.AlertId).NotEmpty();
    }
}
