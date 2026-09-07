using FluentValidation;

namespace HR.Modules.Marketing.Features.UpdateMarketingRoadmapItem;

internal sealed class UpdateMarketingRoadmapItemValidator : AbstractValidator<UpdateMarketingRoadmapItemRequest>
{
    public UpdateMarketingRoadmapItemValidator()
    {
        RuleFor(r => r.Id).NotEmpty();

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
