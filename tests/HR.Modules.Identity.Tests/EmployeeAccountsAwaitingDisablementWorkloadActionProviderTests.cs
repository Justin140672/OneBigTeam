using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// OBT-721 workload action provider tests for employee accounts awaiting disablement. HR-only.
/// Composes IOffboardingReportReader (employees past their LastWorkingDay, plan not yet
/// Completed) with IdentityDbContext.Users (still-active accounts for those employees).
/// </summary>
public class EmployeeAccountsAwaitingDisablementWorkloadActionProviderTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    private static ClaimsPrincipal AnyCaller() => new(new ClaimsIdentity());

    private static IdentityDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new IdentityDbContext(options);
    }

    private static OffboardingReportItem BuildOffboardingItem(Guid employeeId, DateOnly lastWorkingDay, string status) =>
        new(employeeId, lastWorkingDay, status, 1, 0, [], [], DocumentsReturned: false);

    private static ApplicationUser CreateActiveUser(Guid employeeId) =>
        ApplicationUser.Create(employeeId, $"{employeeId:N}@example.com", "hash", "First", "Last", DateTimeOffset.UtcNow);

    [Fact]
    public async Task HrCaller_Sees_Still_Active_Accounts_Past_LastWorkingDay_CompanyWide()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        context.Users.Add(CreateActiveUser(employeeId));
        await context.SaveChangesAsync();

        var offboardingReader = new FakeOffboardingReportReader(
        [
            BuildOffboardingItem(employeeId, Today.AddDays(-1), "InProgress"),
        ]);

        var provider = new EmployeeAccountsAwaitingDisablementWorkloadActionProvider(
            context, offboardingReader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(employeeId, action.EmployeeId);
    }

    [Fact]
    public async Task NonHrCaller_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        context.Users.Add(CreateActiveUser(employeeId));
        await context.SaveChangesAsync();

        var offboardingReader = new FakeOffboardingReportReader(
        [
            BuildOffboardingItem(employeeId, Today.AddDays(-1), "InProgress"),
        ]);

        var provider = new EmployeeAccountsAwaitingDisablementWorkloadActionProvider(
            context, offboardingReader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Excludes_Employees_Whose_Account_Is_Already_Disabled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var user = CreateActiveUser(employeeId);
        user.Deactivate(DateTimeOffset.UtcNow);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var offboardingReader = new FakeOffboardingReportReader(
        [
            BuildOffboardingItem(employeeId, Today.AddDays(-1), "InProgress"),
        ]);

        var provider = new EmployeeAccountsAwaitingDisablementWorkloadActionProvider(
            context, offboardingReader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Excludes_Employees_With_Completed_Offboarding_Or_Future_LastWorkingDay()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedEmployeeId = Guid.NewGuid();
        var futureEmployeeId = Guid.NewGuid();
        context.Users.AddRange(CreateActiveUser(completedEmployeeId), CreateActiveUser(futureEmployeeId));
        await context.SaveChangesAsync();

        var offboardingReader = new FakeOffboardingReportReader(
        [
            BuildOffboardingItem(completedEmployeeId, Today.AddDays(-1), "Completed"),
            BuildOffboardingItem(futureEmployeeId, Today.AddDays(5), "InProgress"),
        ]);

        var provider = new EmployeeAccountsAwaitingDisablementWorkloadActionProvider(
            context, offboardingReader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Maps_ActionType_Category_Status_DueDate_And_DeepLink()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var lastWorkingDay = Today.AddDays(-2);
        context.Users.Add(CreateActiveUser(employeeId));
        await context.SaveChangesAsync();

        var offboardingReader = new FakeOffboardingReportReader(
        [
            BuildOffboardingItem(employeeId, lastWorkingDay, "InProgress"),
        ]);

        var provider = new EmployeeAccountsAwaitingDisablementWorkloadActionProvider(
            context, offboardingReader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Disable Account", action.ActionType);
        Assert.Equal("Employee Accounts Awaiting Disablement", action.ActionCategory);
        Assert.Equal("Access Not Yet Disabled", action.Status);
        Assert.Equal(lastWorkingDay, action.DueDate);
        Assert.Equal($"/companies/{companyId}/user-administration/{employeeId}", action.DeepLinkUrl);
    }
}
