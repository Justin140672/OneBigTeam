using HR.Modules.Employees.Contracts;

namespace HR.Infrastructure.Abstractions;

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
