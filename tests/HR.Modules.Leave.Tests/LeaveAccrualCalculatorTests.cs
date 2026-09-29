using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Tests;

public class LeaveAccrualCalculatorTests
{

    [Fact]
    public void None_Returns_Full_Entitlement_On_AccrualStartDate()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            25m, AccrualMethod.None,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 1, 1));

        Assert.Equal(25m, result);
    }

    [Fact]
    public void None_Returns_Full_Entitlement_Mid_Year()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            25m, AccrualMethod.None,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 6, 15));

        Assert.Equal(25m, result);
    }

    [Fact]
    public void None_Returns_Zero_Before_AccrualStartDate()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            25m, AccrualMethod.None,
            accrualStartDate: new DateOnly(2026, 6, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 5, 31));

        Assert.Equal(0m, result);
    }


    [Fact]
    public void Annual_Returns_Full_Entitlement_Upfront_From_AccrualStartDate()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            25m, AccrualMethod.Annual,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 1, 1));

        Assert.Equal(25m, result);
    }

    [Fact]
    public void Annual_Returns_Zero_Before_AccrualStartDate()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            25m, AccrualMethod.Annual,
            accrualStartDate: new DateOnly(2026, 6, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 5, 31));

        Assert.Equal(0m, result);
    }


    [Fact]
    public void Monthly_Returns_Zero_Before_AccrualStartDate()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            24m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2025, 12, 31));

        Assert.Equal(0m, result);
    }

    [Fact]
    public void Monthly_Returns_Partial_Fraction_Mid_Period()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            22m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 6, 1));

        Assert.Equal(10.0m, result);
    }

    [Fact]
    public void Monthly_Returns_Full_Entitlement_Exactly_At_PolicyYearEnd()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            24m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 12, 31));

        Assert.Equal(24m, result);
    }

    [Fact]
    public void Monthly_Caps_At_Full_Entitlement_When_AsOfDate_Is_After_PolicyYearEnd()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            24m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2027, 3, 1));

        Assert.Equal(24m, result);
    }


    [Fact]
    public void Fortnightly_Returns_Zero_Before_AccrualStartDate()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            26m, AccrualMethod.Fortnightly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2025, 12, 31));

        Assert.Equal(0m, result);
    }

    [Fact]
    public void Fortnightly_Returns_Partial_Fraction_Mid_Period()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            26m, AccrualMethod.Fortnightly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 1, 29));

        Assert.Equal(2.0m, result);
    }

    [Fact]
    public void Fortnightly_Returns_Full_Entitlement_Exactly_At_PolicyYearEnd()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            26m, AccrualMethod.Fortnightly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 12, 31));

        Assert.Equal(26m, result);
    }


    [Fact]
    public void Monthly_Handles_Leap_Year_PolicyYearEnd_Correctly()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            22m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2028, 1, 1),
            policyYearEnd: new DateOnly(2028, 12, 31),
            asOfDate: new DateOnly(2028, 3, 1));

        Assert.Equal(4.0m, result);
    }

    [Fact]
    public void Fortnightly_Handles_Period_Crossing_Leap_Day_Correctly()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            26m, AccrualMethod.Fortnightly,
            accrualStartDate: new DateOnly(2028, 1, 1),
            policyYearEnd: new DateOnly(2028, 12, 31),
            asOfDate: new DateOnly(2028, 3, 1));

        Assert.Equal(4.0m, result);
    }


    [Fact]
    public void Monthly_Paces_Correctly_For_NonJanuary_Policy_Year()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            22m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 4, 1),
            policyYearEnd: new DateOnly(2027, 3, 31),
            asOfDate: new DateOnly(2026, 9, 1));

        Assert.Equal(10.0m, result);
    }


    [Fact]
    public void Monthly_Joiner_Earns_Full_ProRated_Entitlement_Exactly_By_PolicyYearEnd()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            14.5m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 6, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 12, 31));

        Assert.Equal(14.5m, result);
    }

    [Fact]
    public void Monthly_Joiner_Never_Exceeds_ProRated_Entitlement_When_AsOfDate_Is_Past_PolicyYearEnd()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            14.5m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 6, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2027, 6, 1));

        Assert.Equal(14.5m, result);
    }


    [Fact]
    public void Monthly_Rounds_Accrued_Days_Down_To_Nearest_HalfDay()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            25m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 4, 1),
            asOfDate: new DateOnly(2026, 3, 1));

        Assert.Equal(16.5m, result);
    }

    [Fact]
    public void Monthly_Rounds_Value_Just_Below_A_HalfDay_Boundary_Down_To_Boundary_Below()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            10m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 4, 1),
            asOfDate: new DateOnly(2026, 2, 1));

        Assert.Equal(3.0m, result);
    }

    [Fact]
    public void Monthly_Does_Not_Round_Value_Already_Exactly_On_A_HalfDay_Boundary()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            21m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 4, 1),
            asOfDate: new DateOnly(2026, 2, 1));

        Assert.Equal(7.0m, result);
    }


    [Fact]
    public void Monthly_Grants_Full_Entitlement_Immediately_When_Joiner_Has_Less_Than_One_Period_Remaining()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            0.5m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 12, 20),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 12, 20));

        Assert.Equal(0.5m, result);
    }

    [Fact]
    public void Fortnightly_Grants_Full_Entitlement_Immediately_When_Joiner_Has_Less_Than_One_Period_Remaining()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            0.5m, AccrualMethod.Fortnightly,
            accrualStartDate: new DateOnly(2026, 12, 20),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 12, 20));

        Assert.Equal(0.5m, result);
    }


    [Fact]
    public void Returns_Zero_When_ProRatedEntitlementDays_Is_Zero_Regardless_Of_Method()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            0m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 12, 31));

        Assert.Equal(0m, result);
    }

    [Fact]
    public void Returns_Zero_When_ProRatedEntitlementDays_Is_Negative_Regardless_Of_Method()
    {
        // Defensive case: a negative pro-rated entitlement should never occur upstream, but the
        // <= 0 short-circuit must not accidentally let a negative value flow through and be
        // floored/reported as a small negative balance.
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            -5m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 6, 1));

        Assert.Equal(0m, result);
    }

    [Fact]
    public void Monthly_Returns_Zero_When_AsOfDate_Equals_AccrualStartDate()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            24m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 1, 1));

        Assert.Equal(0m, result);
    }
}
