namespace HR.Modules.Leave.Domain;

/// <summary>
/// Single source of truth for pro-rating an employee's leave entitlement for the leave year in
/// which they start. All entitlement-creating paths (manual employee creation, employee import)
/// route through <see cref="EmployeeCreatedHandler"/> and therefore this calculator, so they
/// always produce identical results for an equivalent start date and full-year entitlement.
///
/// An employee starting on or before the first day of the company's leave year receives full
/// entitlement, no pro-rating. An employee starting after the leave year has ended receives zero
/// entitlement for that (already elapsed) policy year. Otherwise entitlement is scaled by the
/// fraction of the leave year remaining from the employee's start date, using calendar days so the
/// calculation works for any company-configured leave year (never hard-codes Jan-Dec).
/// </summary>
internal static class LeaveEntitlementCalculator
{
    public static decimal CalculateEntitlement(
        decimal fullYearEntitlementDays,
        DateOnly leaveYearStart,
        DateOnly leaveYearEnd,
        DateOnly employeeStartDate)
    {
        if (employeeStartDate <= leaveYearStart)
            return fullYearEntitlementDays;

        if (employeeStartDate > leaveYearEnd)
            return 0m;

        var totalDaysInYear = leaveYearEnd.DayNumber - leaveYearStart.DayNumber + 1;
        var remainingDays = leaveYearEnd.DayNumber - employeeStartDate.DayNumber + 1;

        var proRated = fullYearEntitlementDays * remainingDays / totalDaysInYear;

        return Math.Round(proRated, 2, MidpointRounding.AwayFromZero);
    }
}
