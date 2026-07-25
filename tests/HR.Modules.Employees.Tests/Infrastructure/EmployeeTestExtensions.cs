using System.Reflection;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Tests.Infrastructure;

/// <summary>
/// Employee no longer exposes a Terminate()-equivalent domain method for the Leaving/FormerEmployee
/// statuses — those transitions are only introduced by the Employee Leaving Process feature's later
/// slices (the "Start Leaving Process" action, and the scheduled job that follows it). Tests that
/// need an employee already in one of these states purely for exclusion-filter scenarios use this
/// reflection helper instead of a public domain mutator, so the domain model itself stays clean of
/// test-only setters.
/// </summary>
internal static class EmployeeTestExtensions
{
    public static void SetStatusForTesting(this Employee employee, EmploymentStatus status, DateTimeOffset now)
    {
        typeof(Employee).GetProperty(nameof(Employee.Status), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(employee, status);
        typeof(Employee).GetProperty(nameof(Employee.UpdatedAt), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(employee, now);
    }
}
