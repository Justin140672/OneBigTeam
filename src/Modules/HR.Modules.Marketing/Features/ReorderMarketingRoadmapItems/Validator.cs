using FluentValidation;

namespace HR.Modules.Marketing.Features.ReorderMarketingRoadmapItems;

internal sealed class ReorderMarketingRoadmapItemsValidator
    : AbstractValidator<ReorderMarketingRoadmapItemsRequest>
{
    public ReorderMarketingRoadmapItemsValidator()
    {
        RuleFor(r => r.OrderedIds)
            .NotNull()
            .Must(ids => ids is not null && ids.Count > 0)
            .WithMessage("At least one roadmap item id is required.")
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Ordered ids must not contain duplicates.");
    }
}
