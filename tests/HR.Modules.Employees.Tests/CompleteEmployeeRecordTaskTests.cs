using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services.OnboardingTasks;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CompleteEmployeeRecordTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);
    private static readonly DateOnly Dob = new(1990, 5, 20);

    [Fact]
    public async Task IsCompletedAsync_Returns_True_When_Company_Has_No_Employees()
    {
        await using var context = BuildContext();

        var task = new CompleteEmployeeRecordTask(context);

        var result = await task.IsCompletedAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_True_When_No_Employee_Requires_Initial_Setup()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.Add(CreateEmployee(companyId, "alice@example.com", requiresInitialSetup: false));
        await context.SaveChangesAsync();

        var task = new CompleteEmployeeRecordTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_At_Least_One_Employee_Requires_Initial_Setup()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.Add(CreateEmployee(companyId, "alice@example.com", requiresInitialSetup: false));
        context.Employees.Add(CreateEmployee(companyId, "bob@example.com", requiresInitialSetup: true));
        await context.SaveChangesAsync();

        var task = new CompleteEmployeeRecordTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Ignores_Employees_Requiring_Setup_In_Other_Companies()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        context.Employees.Add(CreateEmployee(companyId, "alice@example.com", requiresInitialSetup: false));
        context.Employees.Add(CreateEmployee(otherCompanyId, "bob@example.com", requiresInitialSetup: true));
        await context.SaveChangesAsync();

        var task = new CompleteEmployeeRecordTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.True(result);
    }

    private static Employee CreateEmployee(Guid companyId, string workEmail, bool requiresInitialSetup)
    {
        var employee = Employee.Create(
            Guid.NewGuid(),
            companyId,
            "Alice",
            "Smith",
            workEmail,
            StartDate,
            hasSystemAccess: false,
            Dob,
            "British",
            "Female",
            $"EMP-{Guid.NewGuid():N}"[..12],
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Now);

        if (requiresInitialSetup)
            employee.MarkRequiresInitialSetup(Now);

        return employee;
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options);
    }
}
