using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Services;
using HR.Modules.Offboarding.Tests.Infrastructure;

namespace HR.Modules.Offboarding.Tests;

/// <summary>
/// OBT-721 workload action provider tests for outstanding offboarding tasks — HR-only (see xmldoc
/// on the provider). Offboarding has no manager-scoped tier, unlike onboarding/probation, so a
/// Manager caller must get nothing back regardless of any other role.
/// </summary>
public class OutstandingOffboardingTasksWorkloadActionProviderTests
{
    private static readonly DateOnly Today = new(2026, 7, 29);

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static OffboardingReportItem BuildItem(
        Guid employeeId, DateOnly lastWorkingDay, params string[] outstandingTaskTitles) =>
        new(employeeId, lastWorkingDay, "InProgress", outstandingTaskTitles.Length, 0,
            outstandingTaskTitles, [], DocumentsReturned: false);

    [Fact]
    public async Task HrCaller_Sees_All_Outstanding_Offboarding_Tasks_CompanyWide()
    {
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(employeeA, Today.AddDays(5), "Return laptop"),
            BuildItem(employeeB, Today.AddDays(10), "Exit interview"),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task NonHrCaller_Returns_Empty_Even_With_Manager_Role()
    {
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(Guid.NewGuid(), Today.AddDays(5), "Return laptop"),
        ]);

        // Manager (or any other) role, but not HR — this category has no manager-scoped tier.
        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-onboarding"));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CallerWithNoRole_Returns_Empty_Not_Throws()
    {
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(Guid.NewGuid(), Today.AddDays(5), "Return laptop"),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Employees_With_No_Outstanding_Tasks_Are_Excluded()
    {
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(Guid.NewGuid(), Today.AddDays(5)), // no outstanding task titles
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Maps_ActionType_Category_DeepLink_And_Overdue_Status()
    {
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var pastDueDate = Today.AddDays(-3);
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(employeeId, pastDueDate, "Return laptop"),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Return laptop", action.ActionType);
        Assert.Equal("Outstanding Offboarding Tasks", action.ActionCategory);
        Assert.Equal("Overdue", action.Status);
        Assert.Equal(pastDueDate, action.DueDate);
        Assert.Equal($"/companies/{companyId}/employees/{employeeId}/view", action.DeepLinkUrl);
    }
}
