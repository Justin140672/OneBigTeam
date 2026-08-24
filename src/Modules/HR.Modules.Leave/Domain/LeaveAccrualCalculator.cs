namespace HR.Modules.Leave.Domain;

/// <summary>
/// Single source of truth for how much of a leave-type's (already joiner-pro-rated) annual
/// entitlement has actually accrued as of a given date (LEAVE-04). Reused identically by balance
/// display (GetEmployeeLeaveBalanceHandler), request validation (SubmitLeaveRequestHandler,
/// PreviewLeaveRequestHandler) and the negative-balance guard (AdjustLeaveBalanceHandler) so the
/// figure shown to a user and the figure enforced at submission time can never diverge.
///
/// This is a pure, side-effect-free calculation performed on read - there is no periodic mutating
/// "accrual job" for Monthly/Fortnightly leave types. That is a deliberate design choice: because
/// accrual only ever gates how much of an already-known, already-stored annual entitlement is
/// currently available (it never itself changes stored balance rows), recomputing it on every read
/// is always safe to repeat, trivially idempotent, and removes an entire class of job-retry/
/// double-accrual bugs that a stateful periodic job would otherwise need to guard against.
///
/// Accrual behaviour by <see cref="AccrualMethod"/>:
///   - None: no periodic restriction at all - the full (joiner-pro-rated) entitlement is available
///     as soon as the balance exists.
///   - Annual: the full entitlement is granted upfront, at the start of the employee's leave year
///     (i.e. from <c>accrualStartDate</c> - the policy year start for an existing employee, or the
///     employee's own start date for a joiner's first, partial, policy year). This is a documented
///     product decision for LEAVE-04: "Annual" was not already defined upfront-vs-year-end anywhere
///     in the existing specs, so upfront was chosen as it matches how DefaultEntitlementDays/
///     LeaveEntitlementCalculator already grant a joiner's full pro-rated entitlement immediately on
///     day one, and it is the least surprising default for a typical UK-style annual leave policy.
///   - Monthly / Fortnightly: entitlement accrues in equal instalments, one per complete calendar
///     month / 14-day period elapsed since <c>accrualStartDate</c>, reaching exactly the full
///     (joiner-pro-rated) entitlement by <c>policyYearEnd</c>. Using <c>accrualStartDate</c> as both
///     the numerator and denominator anchor (rather than always dividing by a fixed 12/26) is what
///     prevents "double-reducing" a joiner: their entitlement was already pro-rated down for their
///     partial year by LeaveEntitlementCalculator, so periodic accrual must pace out *that already-
///     reduced total* across *their own remaining periods*, not a full calendar year of periods.
///
/// Rounding rule (documented per LEAVE-04): accrued days for Monthly/Fortnightly are rounded DOWN
/// (floored) to 2 decimal places - matching the numeric(6,2) precision already used for
/// leave_balances.entitlement_days/leave_types default entitlement - so an employee is never shown
/// or allowed to book more leave than they have strictly earned as of today. Full/upfront accrual
/// (None/Annual) is not rounded since it is simply the already-rounded stored entitlement.
/// </summary>
internal static class LeaveAccrualCalculator
{
    public static decimal CalculateAccruedDays(
        decimal proRatedEntitlementDays,
        AccrualMethod accrualMethod,
        DateOnly accrualStartDate,
        DateOnly policyYearEnd,
        DateOnly asOfDate)
    {
        if (proRatedEntitlementDays <= 0m)
            return 0m;

        if (asOfDate < accrualStartDate)
            return 0m;

        return accrualMethod switch
        {
            AccrualMethod.Monthly => AccruePeriodic(proRatedEntitlementDays, accrualStartDate, policyYearEnd, asOfDate, periodMonths: 1),
            AccrualMethod.Fortnightly => AccruePeriodic(proRatedEntitlementDays, accrualStartDate, policyYearEnd, asOfDate, periodDays: 14),
            // None and Annual both grant the full entitlement upfront - see class remarks.
            _ => proRatedEntitlementDays
        };
    }

    private static decimal AccruePeriodic(
        decimal entitlement,
        DateOnly accrualStartDate,
        DateOnly policyYearEnd,
        DateOnly asOfDate,
        int? periodMonths = null,
        int? periodDays = null)
    {
        var cappedAsOfDate = asOfDate > policyYearEnd ? policyYearEnd : asOfDate;

        var totalPeriods = CountCompletePeriods(accrualStartDate, policyYearEnd, periodMonths, periodDays);

        // Fewer than one full accrual period exists between the employee's start and the end of
        // the policy year (e.g. they joined in the final week of the year) - grant the entitlement
        // in full immediately rather than dividing by zero or withholding it indefinitely.
        if (totalPeriods <= 0)
            return entitlement;

        var periodsElapsed = Math.Min(
            CountCompletePeriods(accrualStartDate, cappedAsOfDate, periodMonths, periodDays),
            totalPeriods);

        var accrued = entitlement * periodsElapsed / totalPeriods;

        return RoundDown(accrued);
    }

    private static int CountCompletePeriods(DateOnly from, DateOnly to, int? periodMonths, int? periodDays)
    {
        if (to <= from)
            return 0;

        if (periodMonths is { } months)
        {
            var count = 0;
            var cursor = from;

            while (true)
            {
                var next = cursor.AddMonths(months);
                if (next > to)
                    break;

                count++;
                cursor = next;
            }

            return count;
        }

        return (to.DayNumber - from.DayNumber) / periodDays!.Value;
    }

    private static decimal RoundDown(decimal value) => Math.Floor(value * 100m) / 100m;
}
