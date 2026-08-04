using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services.OnboardingTasks;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ImportEmployeesTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);
    private static readonly DateOnly Dob = new(1990, 5, 20);

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_Company_Has_No_Employees()
    {
        await using var context = BuildContext();

        var task = new ImportEmployeesTask(context);

        var result = await task.IsCompletedAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_Company_Has_Exactly_One_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.Add(CreateEmployee(companyId, "alice@example.com"));
        await context.SaveChangesAsync();

        var task = new ImportEmployeesTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_True_When_Company_Has_Two_Or_More_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.Add(CreateEmployee(companyId, "alice@example.com"));
        context.Employees.Add(CreateEmployee(companyId, "bob@example.com"));
        await context.SaveChangesAsync();

        var task = new ImportEmployeesTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.True(result);
    }

    private static Employee CreateEmployee(Guid companyId, string workEmail)
    {
        return Employee.Create(
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
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options);
    }
}
