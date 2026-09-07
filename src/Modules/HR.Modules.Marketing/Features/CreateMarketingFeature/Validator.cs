using FluentValidation;

using HR.Modules.Marketing.Domain;

namespace HR.Modules.Marketing.Features.CreateMarketingFeature;

internal sealed class CreateMarketingFeatureValidator : AbstractValidator<CreateMarketingFeatureRequest>
{
    public CreateMarketingFeatureValidator()
    {
        RuleFor(r => r.Slug)
            .NotEmpty()
            .MaximumLength(100)
            .Must(slug => MarketingFeature.SlugPattern.IsMatch((slug ?? string.Empty).Trim().ToLowerInvariant()))
            .WithMessage("Slug must be lower-case alphanumeric words separated by single hyphens.");

        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Summary)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(r => r.IconName)
            .MaximumLength(100);

        RuleFor(r => r.YouTubeId)
            .MaximumLength(32);

        RuleFor(r => r.DisplayOrder)
            .GreaterThanOrEqualTo(0);
    }
}
