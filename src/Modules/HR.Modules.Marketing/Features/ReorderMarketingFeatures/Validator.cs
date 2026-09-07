using FluentValidation;

namespace HR.Modules.Marketing.Features.ReorderMarketingFeatures;

internal sealed class ReorderMarketingFeaturesValidator : AbstractValidator<ReorderMarketingFeaturesRequest>
{
    public ReorderMarketingFeaturesValidator()
    {
        RuleFor(r => r.OrderedIds)
            .NotNull()
            .Must(ids => ids is not null && ids.Count > 0)
            .WithMessage("At least one feature id is required.")
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Ordered ids must not contain duplicates.");
    }
}
