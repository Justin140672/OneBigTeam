namespace HR.Modules.Leave.Domain;

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

        return RoundToNearestHalfDay(proRated);
    }

    internal static decimal RoundToNearestHalfDay(decimal value) =>
        Math.Round(value * 2m, MidpointRounding.AwayFromZero) / 2m;
}
