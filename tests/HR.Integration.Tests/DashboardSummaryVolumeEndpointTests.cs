using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DashboardSummaryVolumeEndpointTests
{
    private const int CategoryDisplayCap = 25;

    private readonly ApiWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.Date);

    public DashboardSummaryVolumeEndpointTests(ApiWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Theory]
    [InlineData("small", 25)]
    [InlineData("medium", 500)]
    [InlineData("large", 5000)]
    public async Task ManagerDashboardSummary_At_Company_Scale_Stays_Bounded_And_Counts_Full_Volume(
        string sizeLabel, int employeeCount)
    {
        var companyId = Guid.NewGuid();

        var employeeIds = await SeedEmployeesAsync(companyId, employeeCount);
        var expectedPendingLeave = employeeCount / 5;
        var expectedOverdueTasks = employeeCount / 10;
        await SeedPendingLeaveAsync(companyId, employeeIds.Take(expectedPendingLeave));
        await SeedOverdueTasksAsync(companyId, employeeIds.Take(expectedOverdueTasks));

        var userId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync($"/api/companies/{companyId}/dashboards/hr/summary");
        stopwatch.Stop();

        _output.WriteLine(
            $"[{sizeLabel}] {employeeCount} employees, {expectedPendingLeave} pending leave, " +
            $"{expectedOverdueTasks} overdue tasks -> summary request {stopwatch.ElapsedMilliseconds} ms");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);

        Assert.All(payload!.Categories, c => Assert.True(
            c.Items.Count <= CategoryDisplayCap,
            $"category '{c.Category}' returned {c.Items.Count} items, expected <= {CategoryDisplayCap}"));

        Assert.All(payload.Categories, c => Assert.True(
            c.WaitingItems.Count <= CategoryDisplayCap,
            $"category '{c.Category}' returned {c.WaitingItems.Count} waiting items, expected <= {CategoryDisplayCap}"));

        var leave = payload.Categories.Single(c => c.Category == "Pending Leave Approvals");
        Assert.Equal(expectedPendingLeave, leave.ActionableCount + leave.WaitingOnOthersCount);

        var tasks = payload.Categories.Single(c => c.Category == "Manager Tasks Overdue");
        Assert.Equal(expectedOverdueTasks, tasks.ActionableCount + tasks.WaitingOnOthersCount);

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"[{sizeLabel}] summary request took {stopwatch.ElapsedMilliseconds} ms (indicative bound only)");
    }

    private async Task<IReadOnlyList<Guid>> SeedEmployeesAsync(Guid companyId, int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);

        var employees = Enumerable.Range(0, count)
            .Select(i => Employee.Create(
                Guid.NewGuid(), companyId, "Vol", $"Employee{i}",
                $"vol.{i}.{Guid.NewGuid():N}@example.com",
                new DateOnly(2026, 1, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1),
                "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
                refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId,
                Now.AddSeconds(i)))
            .ToList();

        db.Employees.AddRange(employees);
        await db.SaveChangesAsync();
        return employees.Select(e => e.Id).ToList();
    }

    private async Task SeedPendingLeaveAsync(Guid companyId, IEnumerable<Guid> employeeIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var requests = employeeIds.Select((employeeId, i) => LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
            Today.AddDays(5), LeaveDayPart.FullDay, Today.AddDays(8), LeaveDayPart.FullDay,
            3m, "Trip", Now.AddSeconds(i)));
        db.LeaveRequests.AddRange(requests);
        await db.SaveChangesAsync();
    }

    private async Task SeedOverdueTasksAsync(Guid companyId, IEnumerable<Guid> employeeIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var tasks = employeeIds.Select((employeeId, i) => TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Complete document check", null,
            TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete, Today.AddDays(-3),
            employeeId, null, Now.AddSeconds(i)));
        db.TaskItems.AddRange(tasks);
        await db.SaveChangesAsync();
    }

    private sealed record SummaryPayload(
        List<CategoryPayload> Categories,
        int TotalActionableCount,
        bool AllRequiredLoaded,
        bool HasPartialFailure,
        DateOnly AsOfDate);

    private sealed record CategoryPayload(
        string Category,
        string Status,
        bool Required,
        int ActionableCount,
        bool IsTruncated,
        List<ActionItemPayload> Items,
        List<ActionItemPayload> WaitingItems,
        int WaitingOnOthersCount);

    private sealed record ActionItemPayload(
        Guid? EmployeeId,
        string EmployeeName,
        string? Department,
        string ActionType,
        string Category,
        DateOnly? DueDate,
        string Urgency,
        bool IsOverdue,
        string Status,
        string DeepLinkUrl,
        Guid? TaskId);
}
