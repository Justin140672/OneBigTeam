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
        // 25 days full entitlement, calendar leave year, employee starts 2026-06-01.
        // 214 remaining days (Jun 1 - Dec 31 inclusive) out of 365 total days.
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 6, 1));

        Assert.Equal(14.66m, result);
    }

    [Fact]
    public void CalculateEntitlement_Returns_Small_ProRated_Amount_For_Starter_Near_LeaveYear_End()
    {
        // Employee starts 2026-12-27, only 5 days remain (27,28,29,30,31) out of 365.
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 27));

        Assert.Equal(0.34m, result); // 25 * 5 / 365 = 0.3424... rounded to 0.34
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
        // April-to-March leave year (proves no Jan-Dec hard-coding). Employee starts 2026-10-01,
        // leave year runs 2026-04-01 to 2027-03-31 (365 days). Remaining days from Oct 1
        // (inclusive) to Mar 31 (inclusive): Oct 31 + Nov 30 + Dec 31 + Jan 31 + Feb 28 + Mar 31 = 182.
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), new DateOnly(2026, 10, 1));

        Assert.Equal(12.47m, result); // 25 * 182 / 365 = 12.4657... rounded to 12.47
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
        // A part-time employee's full-year entitlement (however it was derived) is just another
        // input value — the pro-rating fraction is identical regardless of the magnitude of the
        // full-year figure, so a lower full-time-equivalent entitlement still pro-rates correctly.
        var fullTimeResult = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 6, 1));
        var partTimeResult = LeaveEntitlementCalculator.CalculateEntitlement(
            15m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 6, 1));

        Assert.Equal(14.66m, fullTimeResult);
        Assert.Equal(8.79m, partTimeResult); // 15 * 214 / 365 = 8.7945... rounded to 8.79
    }

    [Fact]
    public void CalculateEntitlement_ProRates_For_MidYear_Leaver_Who_Started_Before_LeaveYear()
    {
        // Employee started well before the leave year (full joiner eligibility), leaves 2026-06-30.
        // Jan 1 - Jun 30 inclusive = 181 days out of 365.
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2020, 1, 1),
            new DateOnly(2026, 6, 30));

        Assert.Equal(12.4m, result); // 25 * 181 / 365 = 12.397... rounded to 12.4
    }

    [Fact]
    public void CalculateEntitlement_ProRates_For_Employee_Who_Both_Joined_And_Left_MidYear()
    {
        // Started 2026-03-01, left 2026-09-30: Mar 1 - Sep 30 inclusive = 214 days out of 365.
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 3, 1),
            new DateOnly(2026, 9, 30));

        Assert.Equal(14.66m, result); // 25 * 214 / 365 = 14.657... rounded to 14.66
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

        Assert.Equal(14.66m, withoutLeavingDateArg);
        Assert.Equal(withoutLeavingDateArg, withExplicitNullLeavingDate);
    }

    [Fact]
    public void CalculateEntitlement_Rounds_AwayFromZero_To_TwoDecimalPlaces_For_Leaver()
    {
        // Started before the leave year, leaves 2026-01-05: 5 days out of 365.
        // 25 * 5 / 365 = 0.34246... which rounds away from zero to 0.34.
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2020, 1, 1),
            new DateOnly(2026, 1, 5));

        Assert.Equal(0.34m, result);
    }

    [Fact]
    public void CalculateEntitlement_ProRates_Leaver_Correctly_For_NonCalendar_LeaveYear()
    {
        // April-to-March leave year. Employee started before the year, leaves 2026-10-31.
        // Apr 1 - Oct 31 inclusive = 30 + 31 + 30 + 31 + 31 + 30 + 31 = 214 days out of 365.
        var result = LeaveEntitlementCalculator.CalculateEntitlement(
            25m, new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), new DateOnly(2020, 1, 1),
            new DateOnly(2026, 10, 31));

        Assert.Equal(14.66m, result); // 25 * 214 / 365 = 14.657... rounded to 14.66
    }
}
