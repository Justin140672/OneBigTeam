using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

/// <summary>
/// OBT-721 workload action provider tests for upcoming employee leaving dates. HR-only, same tier
/// as UpcomingEmployeeStartDatesWorkloadActionProvider. Only in-progress leaving processes with a
/// LastWorkingDay on or after today are surfaced.
/// </summary>
public class UpcomingEmployeeLeavingDatesWorkloadActionProviderTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options);
    }

    private static EmployeeLeavingProcess CreateLeavingProcess(
        Guid companyId, Guid employeeId, DateOnly lastWorkingDay) =>
        EmployeeLeavingProcess.Create(
            Guid.NewGuid(), companyId, employeeId,
            lastWorkingDay.AddDays(1), lastWorkingDay, lastWorkingDay,
            NoticePeriodUnit.Weeks, 4, NoticePeriodSource.Employee, LeavingReason.Resignation,
            Guid.NewGuid(), Now);

    [Fact]
    public async Task HrCaller_Sees_InProgress_Leavers_With_Upcoming_LastWorkingDay_CompanyWide()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.EmployeeLeavingProcesses.AddRange(
            CreateLeavingProcess(companyId, Guid.NewGuid(), Today.AddDays(5)),
            CreateLeavingProcess(companyId, Guid.NewGuid(), Today));
        await context.SaveChangesAsync();

        var provider = new UpcomingEmployeeLeavingDatesWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, new(new ClaimsIdentity()), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task NonHrCaller_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.EmployeeLeavingProcesses.Add(CreateLeavingProcess(companyId, Guid.NewGuid(), Today.AddDays(5)));
        await context.SaveChangesAsync();

        var provider = new UpcomingEmployeeLeavingDatesWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(companyId, new(new ClaimsIdentity()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Excludes_Cancelled_Processes_And_PastLastWorkingDay()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var cancelled = CreateLeavingProcess(companyId, Guid.NewGuid(), Today.AddDays(5));
        cancelled.Cancel("No longer leaving", Now);

        var pastDue = CreateLeavingProcess(companyId, Guid.NewGuid(), Today.AddDays(-1));

        context.EmployeeLeavingProcesses.AddRange(cancelled, pastDue);
        await context.SaveChangesAsync();

        var provider = new UpcomingEmployeeLeavingDatesWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, new(new ClaimsIdentity()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Maps_ActionType_Category_Status_And_DeepLink()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var lastWorkingDay = Today.AddDays(10);
        context.EmployeeLeavingProcesses.Add(CreateLeavingProcess(companyId, employeeId, lastWorkingDay));
        await context.SaveChangesAsync();

        var provider = new UpcomingEmployeeLeavingDatesWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, new(new ClaimsIdentity()), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Prepare for Employee Departure", action.ActionType);
        Assert.Equal("Upcoming Leaving Dates", action.ActionCategory);
        Assert.Equal("Upcoming", action.Status);
        Assert.Equal(lastWorkingDay, action.DueDate);
        Assert.Equal($"/companies/{companyId}/employees/{employeeId}/view", action.DeepLinkUrl);
    }
}
