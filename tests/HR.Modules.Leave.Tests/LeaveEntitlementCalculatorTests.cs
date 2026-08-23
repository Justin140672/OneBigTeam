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
}
