using HR.Web.Services;

namespace HR.Web.Tests;

public class CompensationAdjustmentCalculatorTests
{
    [Fact]
    public void CalculateProposedSalary_PercentageIncrease_Applies_Percentage()
    {
        var result = CompensationAdjustmentCalculator.CalculateProposedSalary(40000m, CompensationAdjustmentMode.PercentageIncrease, 5m);

        Assert.Equal(42000m, result);
    }

    [Fact]
    public void CalculateProposedSalary_FixedAmountIncrease_Adds_Value()
    {
        var result = CompensationAdjustmentCalculator.CalculateProposedSalary(40000m, CompensationAdjustmentMode.FixedAmountIncrease, 2500m);

        Assert.Equal(42500m, result);
    }

    [Fact]
    public void CalculateProposedSalary_SetDirectly_Returns_Value_As_Is()
    {
        var result = CompensationAdjustmentCalculator.CalculateProposedSalary(40000m, CompensationAdjustmentMode.SetDirectly, 55000m);

        Assert.Equal(55000m, result);
    }

    [Fact]
    public void CalculateProposedSalary_Rounds_To_Two_Decimal_Places_Away_From_Zero()
    {
        var result = CompensationAdjustmentCalculator.CalculateProposedSalary(33333.333m, CompensationAdjustmentMode.FixedAmountIncrease, 0.005m);

        Assert.Equal(33333.34m, result);
    }

    [Fact]
    public void CalculateProposedSalary_Throws_For_Unsupported_Mode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CompensationAdjustmentCalculator.CalculateProposedSalary(40000m, (CompensationAdjustmentMode)999, 5m));
    }

    [Fact]
    public void CalculateDifference_Returns_Proposed_Minus_Current_Rounded()
    {
        var result = CompensationAdjustmentCalculator.CalculateDifference(40000m, 42500.555m);

        Assert.Equal(2500.56m, result);
    }

    [Fact]
    public void CalculatePercentageChange_Returns_Correct_Percentage()
    {
        var result = CompensationAdjustmentCalculator.CalculatePercentageChange(40000m, 42000m);

        Assert.Equal(5m, result);
    }

    [Fact]
    public void CalculatePercentageChange_Returns_Null_When_Current_Salary_Is_Zero()
    {
        var result = CompensationAdjustmentCalculator.CalculatePercentageChange(0m, 42000m);

        Assert.Null(result);
    }

    [Fact]
    public void CalculatePercentageChange_Returns_Negative_For_Decrease()
    {
        var result = CompensationAdjustmentCalculator.CalculatePercentageChange(40000m, 38000m);

        Assert.Equal(-5m, result);
    }
}
