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

    [Fact]
    public void Create_Defaults_RequiresInitialSetup_To_False()
    {
        var employee = CreateEmployee("EMP-100");

        Assert.False(employee.RequiresInitialSetup);
        Assert.Null(employee.InitialSetupCompletedAt);
    }

    [Fact]
    public void MarkRequiresInitialSetup_Sets_Flag_And_UpdatedAt()
    {
        var employee = CreateEmployee("EMP-101");
        var later = Now.AddHours(1);

        employee.MarkRequiresInitialSetup(later);

        Assert.True(employee.RequiresInitialSetup);
        Assert.Equal(later, employee.UpdatedAt);
    }

    [Fact]
    public void CompleteInitialSetup_Clears_Flag_And_Sets_CompletedAt()
    {
        var employee = CreateEmployee("EMP-102");
        employee.MarkRequiresInitialSetup(Now);
        var later = Now.AddHours(2);

        employee.CompleteInitialSetup(later);

        Assert.False(employee.RequiresInitialSetup);
        Assert.Equal(later, employee.InitialSetupCompletedAt);
        Assert.Equal(later, employee.UpdatedAt);
    }

    [Fact]
    public void CompleteInitialSetup_Is_Idempotent_When_Called_Without_Prior_MarkRequiresInitialSetup()
    {
        // Guards against a caller invoking CompleteInitialSetup on an employee that never required
        // it — the flag is already false, and the method still no-ops safely rather than throwing.
        var employee = CreateEmployee("EMP-103");

        employee.CompleteInitialSetup(Now);

        Assert.False(employee.RequiresInitialSetup);
        Assert.Equal(Now, employee.InitialSetupCompletedAt);
    }

    [Fact]
    public void CompleteInitialSetup_Called_Twice_Leaves_Flag_False_And_Updates_CompletedAt_To_Latest_Call()
    {
        var employee = CreateEmployee("EMP-104");
        employee.MarkRequiresInitialSetup(Now);
        employee.CompleteInitialSetup(Now.AddHours(1));

        var secondCall = Now.AddHours(2);
        employee.CompleteInitialSetup(secondCall);

        Assert.False(employee.RequiresInitialSetup);
        Assert.Equal(secondCall, employee.InitialSetupCompletedAt);
    }
}
