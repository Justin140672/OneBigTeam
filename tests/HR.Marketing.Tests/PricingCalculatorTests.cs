using HR.Marketing.Services;
using HR.SharedKernel.Pricing;

namespace HR.Marketing.Tests;

public class PricingCalculatorTests
{
    private static SubscriptionPricingConfig Config => SubscriptionPricingConfig.Default;

    [Theory]
    [InlineData(0, 20.00)]
    [InlineData(1, 20.00)]
    [InlineData(10, 20.00)]
    [InlineData(20, 40.00)]
    [InlineData(30, 60.00)]
    [InlineData(50, 100.00)]
    [InlineData(51, 101.75)]
    [InlineData(75, 143.75)]
    [InlineData(100, 187.50)]
    [InlineData(150, 275.00)]
    [InlineData(151, 276.50)]
    [InlineData(200, 350.00)]
    [InlineData(250, 425.00)]
    public void Calculate_ReturnsExpectedMonthlyCost(int employees, decimal expectedMonthly)
    {
        var result = PricingCalculator.Calculate(employees, Config);

        Assert.Equal(expectedMonthly, result.Monthly);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(51)]
    [InlineData(150)]
    [InlineData(151)]
    [InlineData(250)]
    public void Calculate_AnnualCostIsTwelveTimesMonthly(int employees)
    {
        var result = PricingCalculator.Calculate(employees, Config);

        Assert.Equal(result.Monthly * 12, result.Annual);
    }

    [Fact]
    public void Calculate_MinimumMonthlyChargeIsEnforced()
    {
        var result = PricingCalculator.Calculate(1, Config);

        Assert.Equal(20.00m, result.Monthly);
    }

    [Fact]
    public void Calculate_ZeroEmployeesHasZeroEffectivePrice()
    {
        var result = PricingCalculator.Calculate(0, Config);

        Assert.Equal(0m, result.EffectivePricePerEmployee);
    }

    [Theory]
    [InlineData(75, 1.92)]
    [InlineData(150, 1.83)]
    [InlineData(250, 1.70)]
    public void Calculate_EffectivePriceRoundsToTwoDecimalPlaces(int employees, decimal expectedEffectivePrice)
    {
        var result = PricingCalculator.Calculate(employees, Config);

        Assert.Equal(expectedEffectivePrice, Math.Round(result.EffectivePricePerEmployee, 2));
    }

    [Fact]
    public void Calculate_NegativeEmployeeCountIsTreatedAsZero()
    {
        var result = PricingCalculator.Calculate(-5, Config);

        Assert.Equal(0, result.ActiveEmployees);
        Assert.Equal(20.00m, result.Monthly);
    }

    [Fact]
    public void Calculate_SeventyFiveEmployeeWorkedExampleMatchesPricingPageCopy()
    {
        // 50 x £2.00 = £100.00, plus 25 x £1.75 = £43.75, total £143.75/month.
        var result = PricingCalculator.Calculate(75, Config);

        Assert.Equal(143.75m, result.Monthly);
    }

    [Fact]
    public void Calculate_HonoursACustomConfig_NotHardCodedRates()
    {
        var custom = new SubscriptionPricingConfig(
            new[]
            {
                new SubscriptionPricingBand(1, 10, 5.00m),
                new SubscriptionPricingBand(11, null, 3.00m),
            },
            100.00m);

        Assert.Equal(100.00m, PricingCalculator.Calculate(10, custom).Monthly); // 50 -> floored to 100
        Assert.Equal(170.00m, PricingCalculator.Calculate(50, custom).Monthly); // 50 + 120
    }
}
