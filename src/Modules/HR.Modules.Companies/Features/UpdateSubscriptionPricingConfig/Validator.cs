using FluentValidation;

namespace HR.Modules.Companies.Features.UpdateSubscriptionPricingConfig;

/// <summary>
/// Coarse shape validation only — the authoritative structural rules (contiguity, single final
/// unlimited band, gaps/overlaps, final band covering all remaining employees) live in
/// <see cref="HR.SharedKernel.Pricing.SubscriptionPricingConfig.Validate"/> and run in the handler.
/// </summary>
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
