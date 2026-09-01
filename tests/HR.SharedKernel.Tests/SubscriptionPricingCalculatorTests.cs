using HR.SharedKernel.Pricing;

namespace HR.SharedKernel.Tests;

public class SubscriptionPricingCalculatorTests
{
    private static SubscriptionPricingConfig Default => SubscriptionPricingConfig.Default;

    [Theory]
    [InlineData(0, 20.00)]
    [InlineData(1, 20.00)]
    [InlineData(5, 20.00)]
    [InlineData(10, 20.00)]
    [InlineData(50, 100.00)]
    [InlineData(51, 101.75)]
    [InlineData(75, 143.75)]
    [InlineData(150, 275.00)]
    [InlineData(151, 276.50)]
    [InlineData(200, 350.00)]
    [InlineData(1000, 1550.00)]
    public void Calculate_DefaultConfig_ReturnsExpectedFinalMonthlyCharge(int count, decimal expected)
    {
        var breakdown = SubscriptionPricingCalculator.Calculate(count, Default);

        Assert.Equal(expected, breakdown.FinalMonthlyCharge);
    }

    [Fact]
    public void Calculate_NegativeCount_ClampedToZero_AndMinimumApplies()
    {
        var breakdown = SubscriptionPricingCalculator.Calculate(-5, Default);

        Assert.Equal(0, breakdown.ActiveEmployeeCount);
        Assert.Equal(0m, breakdown.CalculatedEmployeeCharge);
        Assert.Equal(20.00m, breakdown.FinalMonthlyCharge);
    }

    [Fact]
    public void Calculate_BandBoundaries_CountEmployeesPerBand()
    {
        var breakdown = SubscriptionPricingCalculator.Calculate(151, Default);

        Assert.Equal(50, breakdown.BandBreakdown[0].EmployeesInBand);
        Assert.Equal(100, breakdown.BandBreakdown[1].EmployeesInBand);
        Assert.Equal(1, breakdown.BandBreakdown[2].EmployeesInBand);
        Assert.Equal(100.00m, breakdown.BandBreakdown[0].Subtotal);
        Assert.Equal(175.00m, breakdown.BandBreakdown[1].Subtotal);
        Assert.Equal(1.50m, breakdown.BandBreakdown[2].Subtotal);
        Assert.Equal(276.50m, breakdown.CalculatedEmployeeCharge);
    }

    [Fact]
    public void Calculate_TwentyFiveEmployees_BelowMinimum_FloorApplies()
    {
        var breakdown = SubscriptionPricingCalculator.Calculate(5, Default);

        Assert.Equal(10.00m, breakdown.CalculatedEmployeeCharge);
        Assert.Equal(20.00m, breakdown.FinalMonthlyCharge);
    }

    [Fact]
    public void Calculate_IsIndependentOfDefaultRates_WithModifiedConfig()
    {
        var config = new SubscriptionPricingConfig(
            new[]
            {
                new SubscriptionPricingBand(1, 10, 5.00m),
                new SubscriptionPricingBand(11, null, 3.00m),
            },
            100.00m);

        // 10 * 5 = 50 -> floored to 100
        Assert.Equal(100.00m, SubscriptionPricingCalculator.Calculate(10, config).FinalMonthlyCharge);
        // 10 * 5 + 10 * 3 = 80 -> floored to 100
        Assert.Equal(100.00m, SubscriptionPricingCalculator.Calculate(20, config).FinalMonthlyCharge);
        // 10 * 5 + 40 * 3 = 170 -> above floor
        Assert.Equal(170.00m, SubscriptionPricingCalculator.Calculate(50, config).FinalMonthlyCharge);
    }

    [Fact]
    public void Validate_Default_IsValid()
    {
        Assert.True(SubscriptionPricingConfig.Default.Validate().IsSuccess);
    }

    [Fact]
    public void Validate_Fails_WhenNoBands()
    {
        var config = new SubscriptionPricingConfig(Array.Empty<SubscriptionPricingBand>(), 20m);

        Assert.True(config.Validate().IsFailure);
    }

    [Fact]
    public void Validate_Fails_WhenFirstBandDoesNotStartAtOne()
    {
        var config = new SubscriptionPricingConfig(
            new[] { new SubscriptionPricingBand(2, null, 1m) }, 20m);

        Assert.True(config.Validate().IsFailure);
    }

    [Fact]
    public void Validate_Fails_OnGap()
    {
        var config = new SubscriptionPricingConfig(
            new[]
            {
                new SubscriptionPricingBand(1, 50, 2m),
                new SubscriptionPricingBand(60, null, 1m),
            },
            20m);

        Assert.True(config.Validate().IsFailure);
    }

    [Fact]
    public void Validate_Fails_OnOverlap()
    {
        var config = new SubscriptionPricingConfig(
            new[]
            {
                new SubscriptionPricingBand(1, 50, 2m),
                new SubscriptionPricingBand(40, null, 1m),
            },
            20m);

        Assert.True(config.Validate().IsFailure);
    }

    [Fact]
    public void Validate_Fails_OnNegativePrice()
    {
        var config = new SubscriptionPricingConfig(
            new[] { new SubscriptionPricingBand(1, null, -1m) }, 20m);

        Assert.True(config.Validate().IsFailure);
    }

    [Fact]
    public void Validate_Fails_OnZeroBoundary()
    {
        var config = new SubscriptionPricingConfig(
            new[] { new SubscriptionPricingBand(1, 0, 1m), new SubscriptionPricingBand(1, null, 1m) }, 20m);

        Assert.True(config.Validate().IsFailure);
    }

    [Fact]
    public void Validate_Fails_OnNegativeMinimum()
    {
        var config = new SubscriptionPricingConfig(
            new[] { new SubscriptionPricingBand(1, null, 1m) }, -0.01m);

        Assert.True(config.Validate().IsFailure);
    }

    [Fact]
    public void Validate_Fails_WhenMultipleUnlimitedBands()
    {
        var config = new SubscriptionPricingConfig(
            new[]
            {
                new SubscriptionPricingBand(1, null, 2m),
                new SubscriptionPricingBand(51, null, 1m),
            },
            20m);

        Assert.True(config.Validate().IsFailure);
    }

    [Fact]
    public void Validate_Fails_WhenFinalBandNotUnlimited()
    {
        var config = new SubscriptionPricingConfig(
            new[]
            {
                new SubscriptionPricingBand(1, 50, 2m),
                new SubscriptionPricingBand(51, 150, 1m),
            },
            20m);

        Assert.True(config.Validate().IsFailure);
    }
}
