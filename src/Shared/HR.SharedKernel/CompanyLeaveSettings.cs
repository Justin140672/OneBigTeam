namespace HR.SharedKernel;

public sealed record CompanyLeaveSettings(
    bool ExcludePublicHolidaysFromLeave,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    WorkingPattern WorkingPattern)
{
    public static CompanyLeaveSettings Default { get; } = new(
        ExcludePublicHolidaysFromLeave: true,
        LeaveYearStartMonth: 1,
        DefaultHolidayAllowance: 25,
        WorkingPattern: WorkingPattern.Default);
}
