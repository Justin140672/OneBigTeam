using FluentValidation;

namespace HR.Modules.Notifications.Features.SendProductUpdate;

internal sealed class SendProductUpdateValidator : AbstractValidator<SendProductUpdateRequest>
{
    public SendProductUpdateValidator()
    {
        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Message)
            .NotEmpty()
            .MaximumLength(4000);

        // Same "application-relative only" invariant NotificationActionRouteBuilder enforces for
        // every other notification type's ActionUrl (NOT-04) — an admin-supplied external/absolute
        // URL must never be stored or followed.
        RuleFor(r => r.Url)
            .Must(url => url!.StartsWith('/') && !url.StartsWith("//"))
            .When(r => !string.IsNullOrWhiteSpace(r.Url))
            .WithMessage("Url must be a relative path starting with '/' (e.g. /reports/recruitment-pipeline).");

        RuleFor(r => r.Url)
            .MaximumLength(2000);
    }
}
