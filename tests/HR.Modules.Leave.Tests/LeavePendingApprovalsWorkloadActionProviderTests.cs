using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

/// <summary>
/// OBT-721 workload action provider tests for pending leave approvals — mirrors the row-scoping
/// coverage pattern established by GetProbationReportHandlerTests (HR sees company-wide, Manager is
/// scoped to direct reports, Manager with no direct reports and non-HR/non-Manager callers get an
/// empty list rather than an exception or company-wide data).
/// </summary>
public class LeavePendingApprovalsWorkloadActionProviderTests
{
    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static LeaveRequest CreatePendingRequest(Guid companyId, Guid employeeId, DateOnly startDate) =>
        LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
            startDate, LeaveDayPart.FullDay,
            startDate.AddDays(3), LeaveDayPart.FullDay,
            4m, "Holiday", DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetActionsAsync_HrCaller_Sees_All_Pending_Requests_CompanyWide()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();

        context.LeaveRequests.AddRange(
            CreatePendingRequest(companyId, employeeA, new DateOnly(2026, 8, 3)),
            CreatePendingRequest(companyId, employeeB, new DateOnly(2026, 8, 10)));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetActionsAsync_ManagerCaller_Is_Scoped_To_DirectReports_Only()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var directReportId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var callerEmployeeId = Guid.NewGuid();

        context.LeaveRequests.AddRange(
            CreatePendingRequest(companyId, directReportId, new DateOnly(2026, 8, 3)),
            CreatePendingRequest(companyId, otherEmployeeId, new DateOnly(2026, 8, 10)));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader([directReportId]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerEmployeeId), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(directReportId, action.EmployeeId);
    }

    [Fact]
    public async Task GetActionsAsync_ManagerWithNoDirectReports_Returns_Empty()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.LeaveRequests.Add(CreatePendingRequest(companyId, Guid.NewGuid(), new DateOnly(2026, 8, 3)));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader([]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActionsAsync_CallerWithNoRecognisedRole_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.LeaveRequests.Add(CreatePendingRequest(companyId, Guid.NewGuid(), new DateOnly(2026, 8, 3)));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader([]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService());

        // No "sub" claim at all — the caller can't even be resolved to an employee id.
        var result = await provider.GetActionsAsync(companyId, new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActionsAsync_Maps_ActionType_Category_DueDate_And_DeepLink()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 8, 3);

        context.LeaveRequests.Add(CreatePendingRequest(companyId, employeeId, startDate));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Approve Leave Request", action.ActionType);
        Assert.Equal("Pending Leave Approvals", action.ActionCategory);
        Assert.Equal(startDate, action.DueDate);
        Assert.Equal($"/companies/{companyId}/employees/{employeeId}/view", action.DeepLinkUrl);
        Assert.Equal("Pending", action.Status);
    }
}
