using FluentValidation;

namespace HR.Modules.Companies.Features.UpdateSubscriptionPricingConfig;

internal sealed class UpdateSubscriptionPricingConfigValidator : AbstractValidator<UpdateSubscriptionPricingConfigRequest>
{
    public UpdateSubscriptionPricingConfigValidator()
    {
        RuleFor(r => r.Bands)
            .NotEmpty();

        RuleFor(r => r.MinimumMonthlyChargeGbp)
            .GreaterThanOrEqualTo(0);

        RuleForEach(r => r.Bands).ChildRules(band =>
        {
            band.RuleFor(b => b.PricePerEmployee).GreaterThanOrEqualTo(0);
            band.RuleFor(b => b.StartEmployee).GreaterThanOrEqualTo(1);
        });
    }
}
