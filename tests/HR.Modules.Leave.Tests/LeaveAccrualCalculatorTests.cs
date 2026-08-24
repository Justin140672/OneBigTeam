using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Tests;

/// <summary>
/// Pure unit tests for <see cref="LeaveAccrualCalculator.CalculateAccruedDays"/> (LEAVE-04). No
/// database or handler wiring involved - see its own class-level XML doc comments for the
/// documented business rules being asserted here.
/// </summary>
public class LeaveAccrualCalculatorTests
{
    // ── AccrualMethod.None ──────────────────────────────────────────────────────

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

    // ── AccrualMethod.Annual ────────────────────────────────────────────────────

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

    // ── AccrualMethod.Monthly ───────────────────────────────────────────────────

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
        // Jan 1 - Dec 31 spans 11 complete monthly periods (Jan1->Feb1...Nov1->Dec1; the 12th
        // would land on Jan1 next year, past policyYearEnd). By June 1, 5 complete periods have
        // elapsed (Jan1->Feb1->Mar1->Apr1->May1->Jun1).
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            22m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 6, 1));

        // 22 * 5/11 = 10.0 exactly.
        Assert.Equal(10.00m, result);
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

    // ── AccrualMethod.Fortnightly ───────────────────────────────────────────────

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
        // 364 days between Jan 1 and Dec 31 2026 / 14 = 26 complete fortnightly periods exactly.
        // By Jan 29 (28 days elapsed), exactly 2 periods have elapsed.
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            26m, AccrualMethod.Fortnightly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 1, 29));

        // 26 * 2/26 = 2.00 exactly.
        Assert.Equal(2.00m, result);
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

    // ── Leap year ────────────────────────────────────────────────────────────────

    [Fact]
    public void Monthly_Handles_Leap_Year_PolicyYearEnd_Correctly()
    {
        // 2028 is a leap year; policy year runs Mar 1 2028 - Feb 28/29 2029... use a simpler
        // same-leap-year span: Jan 1 2028 - Dec 31 2028, with Feb 29 2028 falling mid-year.
        // Complete monthly periods Jan1->Dec1 = 11 (same shape as the non-leap case; February's
        // extra day does not change month-boundary counting since AddMonths is calendar-aware).
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            22m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2028, 1, 1),
            policyYearEnd: new DateOnly(2028, 12, 31),
            asOfDate: new DateOnly(2028, 3, 1)); // spans Feb 29

        // Jan1->Feb1 (1), Feb1->Mar1 (2) - 2 periods elapsed out of 11 total.
        // 22 * 2/11 = 4.00 exactly.
        Assert.Equal(4.00m, result);
    }

    [Fact]
    public void Fortnightly_Handles_Period_Crossing_Leap_Day_Correctly()
    {
        // Period boundary Feb 15 2028 -> Mar 1 2028 (14 days) crosses Feb 29. Day-based counting
        // is unaffected by the leap day since it operates on absolute DayNumber differences.
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            26m, AccrualMethod.Fortnightly,
            accrualStartDate: new DateOnly(2028, 1, 1),
            policyYearEnd: new DateOnly(2028, 12, 31),
            asOfDate: new DateOnly(2028, 3, 1));

        // (2028-03-01 - 2028-01-01) = 60 days elapsed; 60 / 14 = 4 complete periods (56 days),
        // out of (2028-12-31 - 2028-01-01) = 365 days / 14 = 26 total periods.
        // 26 * 4/26 = 4.00 exactly.
        Assert.Equal(4.00m, result);
    }

    // ── Non-January leave year ──────────────────────────────────────────────────

    [Fact]
    public void Monthly_Paces_Correctly_For_NonJanuary_Policy_Year()
    {
        // April-start leave year: Apr 1 2026 - Mar 31 2027. 11 complete monthly periods
        // (Apr1->Mar1 next year is the 11th; the 12th, Mar1->Apr1, lands exactly on policyYearEnd+1
        // day so is excluded since Apr1(2027) > Mar31(2027)).
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            22m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 4, 1),
            policyYearEnd: new DateOnly(2027, 3, 31),
            asOfDate: new DateOnly(2026, 9, 1));

        // Apr1->May1(1)->Jun1(2)->Jul1(3)->Aug1(4)->Sep1(5): 5 periods elapsed out of 11 total.
        // 22 * 5/11 = 10.00 exactly.
        Assert.Equal(10.00m, result);
    }

    // ── Joiner mid-year ──────────────────────────────────────────────────────────

    [Fact]
    public void Monthly_Joiner_Earns_Full_ProRated_Entitlement_Exactly_By_PolicyYearEnd()
    {
        // Joiner starts Jun 1 2026, mid Jan-Dec policy year; already-pro-rated entitlement passed
        // in (14.66, as LeaveEntitlementCalculator would produce). Accrual paces across the
        // joiner's own remaining periods (accrualStartDate to policyYearEnd), never exceeding the
        // pro-rated figure passed in.
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            14.66m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 6, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 12, 31));

        Assert.Equal(14.66m, result);
    }

    [Fact]
    public void Monthly_Joiner_Never_Exceeds_ProRated_Entitlement_When_AsOfDate_Is_Past_PolicyYearEnd()
    {
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            14.66m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 6, 1),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2027, 6, 1));

        Assert.Equal(14.66m, result);
    }

    // ── Rounding boundary ────────────────────────────────────────────────────────

    [Fact]
    public void Monthly_Rounds_Accrued_Days_Down_To_Two_Decimal_Places()
    {
        // 25 * 1/3 = 8.3333... must floor to 8.33, not round to 8.33 via banker's/away-from-zero
        // rounding (which would also give 8.33 here - use a case where rounding up would differ:
        // 25 * 2/3 = 16.6666... floors to 16.66, not 16.67.
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            25m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 1, 1),
            policyYearEnd: new DateOnly(2026, 4, 1), // 3 complete monthly periods total
            asOfDate: new DateOnly(2026, 3, 1)); // 2 periods elapsed

        Assert.Equal(16.66m, result);
    }

    // ── Joiner with fewer than one full period remaining ────────────────────────

    [Fact]
    public void Monthly_Grants_Full_Entitlement_Immediately_When_Joiner_Has_Less_Than_One_Period_Remaining()
    {
        // Joiner starts Dec 20, policy year ends Dec 31 - fewer than one full monthly accrual
        // period remains (totalPeriods <= 0), so the full (already pro-rated) entitlement is
        // granted immediately per the calculator's documented behaviour.
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            0.68m, AccrualMethod.Monthly,
            accrualStartDate: new DateOnly(2026, 12, 20),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 12, 20));

        Assert.Equal(0.68m, result);
    }

    [Fact]
    public void Fortnightly_Grants_Full_Entitlement_Immediately_When_Joiner_Has_Less_Than_One_Period_Remaining()
    {
        // Only 11 days remain between accrualStartDate and policyYearEnd - fewer than one
        // 14-day fortnightly period.
        var result = LeaveAccrualCalculator.CalculateAccruedDays(
            0.68m, AccrualMethod.Fortnightly,
            accrualStartDate: new DateOnly(2026, 12, 20),
            policyYearEnd: new DateOnly(2026, 12, 31),
            asOfDate: new DateOnly(2026, 12, 20));

        Assert.Equal(0.68m, result);
    }

    // ── proRatedEntitlementDays <= 0 short-circuit ──────────────────────────────

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
}
