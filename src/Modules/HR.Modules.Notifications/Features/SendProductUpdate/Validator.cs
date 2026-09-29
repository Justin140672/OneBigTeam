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

        RuleFor(r => r.Url)
            .Must(url => url!.StartsWith('/') && !url.StartsWith("//"))
            .When(r => !string.IsNullOrWhiteSpace(r.Url))
            .WithMessage("Url must be a relative path starting with '/' (e.g. /reports/recruitment-pipeline).");

        RuleFor(r => r.Url)
            .MaximumLength(2000);
    }
}
