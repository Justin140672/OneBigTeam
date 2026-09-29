using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Tests;

public class LeaveEntitlementCalculatorTests
{
    [Fact]
    public void CalculateEntitlement_Returns_Full_Entitlement_When_StartDate_Is_LeaveYear_Start()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1));

        Assert.Equal(25m, result);
    }

    [Fact]
    public void CalculateEntitlement_Returns_Full_Entitlement_When_StartDate_Is_Before_LeaveYear_Start()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2025, 6, 1));

        Assert.Equal(25m, result);
    }

    [Fact]
    public void CalculateEntitlement_ProRates_For_MidYear_Starter_On_Calendar_LeaveYear()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 6, 1));

        Assert.Equal(14.5m, result);
    }

    [Fact]
    public void CalculateEntitlement_Returns_Small_ProRated_Amount_For_Starter_Near_LeaveYear_End()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 27));

        Assert.Equal(0.5m, result);
    }

    [Fact]
    public void CalculateEntitlement_Returns_Zero_When_StartDate_Is_After_LeaveYear_End()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1));

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateEntitlement_ProRates_Correctly_For_NonCalendar_LeaveYear()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), new DateOnly(2026, 10, 1));

        Assert.Equal(12.5m, result);
    }

    [Fact]
    public void CalculateEntitlement_Returns_Full_Entitlement_For_Start_Of_NonCalendar_LeaveYear()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), new DateOnly(2026, 4, 1));

        Assert.Equal(25m, result);
    }

    [Fact]
    public void CalculateEntitlement_ProRates_PartTime_FullYearEntitlement_The_Same_Way()
    {
        var fullTimeResult = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 6, 1));
        var partTimeResult = LeaveEntitlementCalculator.CalculateEntitlement(
            15m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 6, 1));

        Assert.Equal(14.5m, fullTimeResult);
        Assert.Equal(9.0m, partTimeResult);
    }

    [Fact]
    public void CalculateEntitlement_ProRates_For_MidYear_Leaver_Who_Started_Before_LeaveYear()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2020, 1, 1),
            new DateOnly(2026, 6, 30));

        Assert.Equal(12.5m, result);
    }

    [Fact]
    public void CalculateEntitlement_ProRates_For_Employee_Who_Both_Joined_And_Left_MidYear()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 3, 1),
            new DateOnly(2026, 9, 30));

        Assert.Equal(14.5m, result);
    }

    [Fact]
    public void CalculateEntitlement_Returns_Full_Entitlement_When_LeavingDate_Is_LeaveYear_End()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2020, 1, 1),
            new DateOnly(2026, 12, 31));

        Assert.Equal(25m, result);
    }

    [Fact]
    public void CalculateEntitlement_Returns_Full_Entitlement_When_LeavingDate_Is_After_LeaveYear_End()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2020, 1, 1),
            new DateOnly(2027, 3, 1));

        Assert.Equal(25m, result);
    }

    [Fact]
    public void CalculateEntitlement_Returns_Zero_When_LeavingDate_Is_Before_LeaveYear_Start()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2020, 1, 1),
            new DateOnly(2025, 12, 31));

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateEntitlement_With_Null_LeavingDate_Matches_Original_Joiner_Only_Behaviour()
    {
        var withoutLeavingDateArg = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 6, 1));

        var withExplicitNullLeavingDate = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 6, 1), null);

        Assert.Equal(14.5m, withoutLeavingDateArg);
        Assert.Equal(withoutLeavingDateArg, withExplicitNullLeavingDate);
    }

    [Fact]
    public void CalculateEntitlement_Rounds_To_Nearest_HalfDay_For_Leaver()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2020, 1, 1),
            new DateOnly(2026, 1, 5));

        Assert.Equal(0.5m, result);
    }

    [Fact]
    public void CalculateEntitlement_ProRates_Leaver_Correctly_For_NonCalendar_LeaveYear()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), new DateOnly(2020, 1, 1),
            new DateOnly(2026, 10, 31));

        Assert.Equal(14.5m, result);
    }


    [Fact]
    public void CalculateEntitlement_Rounds_Exact_Midpoint_Up_To_Next_HalfDay()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            10m, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 8), new DateOnly(2026, 1, 8));

        Assert.Equal(1.5m, result);
    }

    [Fact]
    public void CalculateEntitlement_Rounds_Exact_Midpoint_Up_To_Next_HalfDay_At_Higher_Magnitude()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            10m, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 8), new DateOnly(2026, 1, 6));

        Assert.Equal(4.0m, result);
    }

    [Fact]
    public void CalculateEntitlement_Rounds_Down_To_Nearest_HalfDay_When_Closer_To_Lower_Half()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            10m, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 8), new DateOnly(2026, 1, 3));

        Assert.Equal(7.5m, result);
    }


    [Fact]
    public void CalculateEntitlement_Returns_Zero_When_FullYearEntitlementDays_Is_Zero()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            0m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 6, 1));

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateEntitlement_Returns_Zero_When_LeavingDate_Equals_StartDate_Outside_LeaveYear()
    {
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 1));

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateEntitlement_Returns_Zero_When_LeavingDate_Is_Before_StartDate()
    {
        // Defensive case: an inconsistent/invalid date pair (leaving before starting) must not
        // produce a negative or nonsensical entitlement - the effective window collapses to empty.
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 6, 30),
            new DateOnly(2026, 6, 1));

        Assert.Equal(0m, result);
    }
}
