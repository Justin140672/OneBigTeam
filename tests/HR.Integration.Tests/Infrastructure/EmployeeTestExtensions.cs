using System.Reflection;
using HR.Modules.Employees.Domain;

namespace HR.Integration.Tests.Infrastructure;

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
