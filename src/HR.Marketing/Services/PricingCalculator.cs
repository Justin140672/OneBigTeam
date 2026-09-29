using HR.SharedKernel.Pricing;

namespace HR.Marketing.Services;

public static class PricingCalculator
{
    public static PricingResult Calculate(int activeEmployees, SubscriptionPricingConfig config)
    {
        var breakdown = SubscriptionPricingCalculator.Calculate(activeEmployees, config);

        var employees = breakdown.ActiveEmployeeCount;
        var monthly = breakdown.FinalMonthlyCharge;
        var annual = monthly * 12;
        var effectivePrice = employees > 0 ? monthly / employees : 0m;

        return new PricingResult(employees, monthly, annual, effectivePrice);
    }
}

public readonly record struct PricingResult(int ActiveEmployees, decimal Monthly, decimal Annual, decimal EffectivePricePerEmployee);
