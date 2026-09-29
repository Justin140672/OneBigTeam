namespace HR.Modules.Leave.Domain;

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

        if (totalPeriods <= 0)
            return entitlement;

        var periodsElapsed = Math.Min(
            CountCompletePeriods(accrualStartDate, cappedAsOfDate, periodMonths, periodDays),
            totalPeriods);

        var accrued = entitlement * periodsElapsed / totalPeriods;

        return RoundDownToHalfDay(accrued);
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

    private static decimal RoundDownToHalfDay(decimal value) => Math.Floor(value * 2m) / 2m;
}
