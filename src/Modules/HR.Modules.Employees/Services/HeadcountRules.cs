using System.Linq.Expressions;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Services;

internal enum HeadcountCategory
{
    Leaver,
    FutureStarter,
    Active,
    Other,
}

// Headcount definition: Total Headcount counts every employee record.
// Each employee falls into exactly one category, so
// Total Headcount = Active + Future Starters + Leavers + Other.
// Precedence: Leaver, then Future Starter, then Active, then Other (suspended / serving notice).
internal static class HeadcountRules
{
    public static HeadcountCategory Classify(
        EmploymentStatus status, DateOnly startDate, DateOnly? leavingDate, DateOnly today)
    {
        if (status == EmploymentStatus.FormerEmployee || (leavingDate is not null && leavingDate <= today))
            return HeadcountCategory.Leaver;

        if (startDate > today)
            return HeadcountCategory.FutureStarter;

        return status == EmploymentStatus.Active ? HeadcountCategory.Active : HeadcountCategory.Other;
    }

    public static Expression<Func<Employee, bool>> ActiveExpression(DateOnly today) =>
        e => e.Status == EmploymentStatus.Active
             && e.StartDate <= today
             && (e.LeavingDate == null || e.LeavingDate > today);
}
