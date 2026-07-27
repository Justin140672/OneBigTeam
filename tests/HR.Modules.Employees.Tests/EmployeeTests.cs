using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Tests;

public class EmployeeTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc));
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static Employee CreateEmployee(string employeeNumber) => Employee.Create(
        Guid.NewGuid(), Guid.NewGuid(), "Alice", "Smith", "alice@example.com", StartDate,
        hasSystemAccess: true, new DateOnly(1990, 5, 20), "British", "Female", employeeNumber,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

    [Theory]
    [InlineData("emp-001", "EMP-001")]
    [InlineData("EMP-001", "EMP-001")]
    [InlineData("  emp-001  ", "EMP-001")]
    [InlineData("007", "007")]
    [InlineData("0Ab", "0AB")]
    public void Create_Normalises_EmployeeNumber_To_Trimmed_Uppercase(string input, string expected)
    {
        var employee = CreateEmployee(input);

        Assert.Equal(expected, employee.EmployeeNumber);
    }

    [Theory]
    [InlineData("emp-002", "EMP-002")]
    [InlineData("  emp-002  ", "EMP-002")]
    [InlineData("007", "007")]
    [InlineData("0Ab", "0AB")]
    public void UpdateEmploymentDetails_Normalises_EmployeeNumber_To_Trimmed_Uppercase(string input, string expected)
    {
        var employee = CreateEmployee("EMP-000");

        employee.UpdateEmploymentDetails(
            input, Guid.NewGuid(), StartDate, null, null, null, null, Now);

        Assert.Equal(expected, employee.EmployeeNumber);
    }
}
