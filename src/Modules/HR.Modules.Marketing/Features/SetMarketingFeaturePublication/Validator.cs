using FluentValidation;

namespace HR.Modules.Marketing.Features.SetMarketingFeaturePublication;

internal sealed class SetMarketingFeaturePublicationValidator : AbstractValidator<SetMarketingFeaturePublicationRequest>
{
    public SetMarketingFeaturePublicationValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
    }
}
