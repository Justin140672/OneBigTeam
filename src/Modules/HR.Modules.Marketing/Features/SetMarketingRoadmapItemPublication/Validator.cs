using FluentValidation;

namespace HR.Modules.Marketing.Features.SetMarketingRoadmapItemPublication;

internal sealed class SetMarketingRoadmapItemPublicationValidator
    : AbstractValidator<SetMarketingRoadmapItemPublicationRequest>
{
    public SetMarketingRoadmapItemPublicationValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
    }
}
