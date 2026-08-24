namespace HR.Modules.Leave.Domain;

/// <summary>
/// Single source of truth for pro-rating an employee's leave entitlement for the leave year in
/// which they start and/or leave. All entitlement-creating paths (manual employee creation,
/// employee import) route through <see cref="EmployeeCreatedHandler"/>, and leaver recalculation
/// (LEAVE-05) routes through the leaving-date-change handlers in
/// Features/RecalculateEntitlementOnLeavingDateChange, so all paths always produce identical
/// results for an equivalent start/leaving date and full-year entitlement.
///
/// An employee whose eligible window (start date through leaving date, if any) fully covers the
/// leave year receives full entitlement, no pro-rating. An employee whose eligible window falls
/// entirely outside the leave year (started after it ended, or left before it began) receives zero
/// entitlement for that policy year. Otherwise entitlement is scaled by the fraction of the leave
/// year covered by the employee's eligible window, using calendar days so the calculation works
/// for any company-configured leave year (never hard-codes Jan-Dec).
///
/// Leaver pro-rating (LEAVE-05) is the mirror image of joiner pro-rating: instead of scaling by
/// days remaining from a late start date, it scales by days elapsed up to (and including) the
/// final eligible employment date — <c>employeeLeavingDate</c>, sourced from
/// EmployeeLeavingProcess.LeavingDate, the same effective date already used elsewhere (e.g.
/// EmployeeDepartureFinalisedIntegrationEvent) as the point employment actually ends. Passing
/// <c>employeeLeavingDate: null</c> (the default) reproduces the original joiner-only calculation
/// exactly, which is what restores the correct entitlement when a leaving process is cancelled —
/// no separate "pre-leaving snapshot" needs to be stored.
/// </summary>
internal static class LeaveEntitlementCalculator
{
    public static decimal CalculateEntitlement(
        decimal fullYearEntitlementDays,
        DateOnly leaveYearStart,
        DateOnly leaveYearEnd,
        DateOnly employeeStartDate,
        DateOnly? employeeLeavingDate = null)
    {
        var effectiveStart = employeeStartDate <= leaveYearStart ? leaveYearStart : employeeStartDate;
        var effectiveEnd = employeeLeavingDate is { } leavingDate && leavingDate < leaveYearEnd
            ? leavingDate
            : leaveYearEnd;

        if (effectiveStart > leaveYearEnd || effectiveEnd < leaveYearStart || effectiveEnd < effectiveStart)
            return 0m;

        if (effectiveStart <= leaveYearStart && effectiveEnd >= leaveYearEnd)
            return fullYearEntitlementDays;

        var totalDaysInYear = leaveYearEnd.DayNumber - leaveYearStart.DayNumber + 1;
        var remainingDays = effectiveEnd.DayNumber - effectiveStart.DayNumber + 1;

        var proRated = fullYearEntitlementDays * remainingDays / totalDaysInYear;

        return Math.Round(proRated, 2, MidpointRounding.AwayFromZero);
    }
}
