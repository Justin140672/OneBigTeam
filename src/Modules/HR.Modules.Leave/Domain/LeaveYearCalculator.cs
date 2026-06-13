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
}
