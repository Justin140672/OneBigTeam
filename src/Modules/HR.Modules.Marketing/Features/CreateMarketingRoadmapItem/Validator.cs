using FluentValidation;

namespace HR.Modules.Marketing.Features.CreateMarketingRoadmapItem;

internal sealed class CreateMarketingRoadmapItemValidator : AbstractValidator<CreateMarketingRoadmapItemRequest>
{
    public CreateMarketingRoadmapItemValidator()
    {
        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .NotEmpty()
            .MaximumLength(4000);

        RuleFor(r => r.IconName)
            .MaximumLength(100);

        RuleFor(r => r.DisplayOrder)
            .GreaterThanOrEqualTo(0);
    }
}
