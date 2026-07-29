using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

/// <summary>
/// OBT-721 workload action provider tests for upcoming employee start dates. HR-only — there is no
/// manager-scoped tier for this category, per the provider's xmldoc.
/// </summary>
public class UpcomingEmployeeStartDatesWorkloadActionProviderTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options);
    }

    private static Employee CreateEmployee(Guid companyId, DateOnly startDate) =>
        Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com",
            startDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task HrCaller_Sees_Starters_Within_Next_30_Days_CompanyWide()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.AddRange(
            CreateEmployee(companyId, Today.AddDays(5)),
            CreateEmployee(companyId, Today.AddDays(29)));
        await context.SaveChangesAsync();

        var provider = new UpcomingEmployeeStartDatesWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, new(new System.Security.Claims.ClaimsIdentity()), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task NonHrCaller_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.Add(CreateEmployee(companyId, Today.AddDays(5)));
        await context.SaveChangesAsync();

        var provider = new UpcomingEmployeeStartDatesWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(companyId, new(new System.Security.Claims.ClaimsIdentity()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Excludes_Starters_Outside_The_30Day_Window()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.Employees.AddRange(
            CreateEmployee(companyId, Today.AddDays(-1)), // already started
            CreateEmployee(companyId, Today.AddDays(31))); // too far out
        await context.SaveChangesAsync();

        var provider = new UpcomingEmployeeStartDatesWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, new(new System.Security.Claims.ClaimsIdentity()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Maps_ActionType_Category_Status_And_DeepLink()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var startDate = Today.AddDays(3);
        var employee = CreateEmployee(companyId, startDate);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var provider = new UpcomingEmployeeStartDatesWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, new(new System.Security.Claims.ClaimsIdentity()), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Prepare for New Starter", action.ActionType);
        Assert.Equal("Upcoming Employee Start Dates", action.ActionCategory);
        Assert.Equal("Upcoming", action.Status);
        Assert.Equal(startDate, action.DueDate);
        Assert.Equal($"/companies/{companyId}/employees/{employee.Id}/view", action.DeepLinkUrl);
    }
}
