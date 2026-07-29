namespace HR.Marketing.Services;

public static class PricingCalculator
{
    public const decimal FirstTierRate = 2.00m;
    public const decimal SecondTierRate = 1.75m;
    public const decimal ThirdTierRate = 1.50m;
    public const int FirstTierLimit = 50;
    public const int SecondTierLimit = 150;
    public const decimal MinimumMonthlyCharge = 20.00m;

    public static PricingResult Calculate(int activeEmployees)
    {
        var employees = Math.Max(0, activeEmployees);

        decimal monthly;
        if (employees <= FirstTierLimit)
        {
            monthly = employees * FirstTierRate;
        }
        else if (employees <= SecondTierLimit)
        {
            monthly = (FirstTierLimit * FirstTierRate) + ((employees - FirstTierLimit) * SecondTierRate);
        }
        else
        {
            monthly = (FirstTierLimit * FirstTierRate)
                + ((SecondTierLimit - FirstTierLimit) * SecondTierRate)
                + ((employees - SecondTierLimit) * ThirdTierRate);
        }

        if (monthly < MinimumMonthlyCharge)
        {
            monthly = MinimumMonthlyCharge;
        }

        var annual = monthly * 12;
        var effectivePrice = employees > 0 ? monthly / employees : 0m;

        return new PricingResult(employees, monthly, annual, effectivePrice);
    }
}

public readonly record struct PricingResult(int ActiveEmployees, decimal Monthly, decimal Annual, decimal EffectivePricePerEmployee);
