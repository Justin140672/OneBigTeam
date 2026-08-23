namespace HR.Modules.Leave.Domain;

internal static class LeaveYearCalculator
{
    public static int GetPolicyYear(DateOnly date, int startMonth)
    {
        if (startMonth <= 1) return date.Year;
        return date.Month >= startMonth ? date.Year : date.Year - 1;
    }

    public static int GetPolicyYear(DateTimeOffset dateTime, int startMonth) =>
        GetPolicyYear(DateOnly.FromDateTime(dateTime.Date), startMonth);

    /// <summary>
    /// Returns the calendar bounds (inclusive start and end dates) of the given policy year, based
    /// on the company's configured leave-year start month. Never hard-codes January-December — a
    /// company configured for e.g. an April-March leave year gets bounds Apr 1 - Mar 31.
    /// </summary>
    public static (DateOnly Start, DateOnly End) GetPolicyYearBounds(int policyYear, int startMonth)
    {
        var effectiveStartMonth = Math.Clamp(startMonth, 1, 12);
        var start = new DateOnly(policyYear, effectiveStartMonth, 1);
        var end = start.AddYears(1).AddDays(-1);
        return (start, end);
    }
}
