using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetManagerDashboardSummaryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.Date);

    public GetManagerDashboardSummaryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/dashboards/manager/summary";

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId, params Guid[] roles)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        foreach (var role in roles)
        {
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, role, companyId);
        }

        return client;
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.Employee);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Returns_Ok_For_Manager()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.Manager);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Returns_Ok_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Response_Always_Carries_Partial_Failure_Contract_Fields()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.Manager);

        var response = await client.GetAsync(Url(companyId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("allRequiredLoaded", out _));
        Assert.True(root.TryGetProperty("hasPartialFailure", out _));
        Assert.True(root.TryGetProperty("totalActionableCount", out _));
        foreach (var category in root.GetProperty("categories").EnumerateArray())
        {
            Assert.True(category.TryGetProperty("status", out _));
            Assert.True(category.TryGetProperty("actionableCount", out _));
        }
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Scopes_Pending_Leave_To_The_Managers_Reporting_Subtree()
    {
        var companyId = Guid.NewGuid();

        var managerId = await SeedEmployeeAsync(companyId, "Meera", "Manager");
        var subManagerId = await SeedEmployeeAsync(companyId, "Sunil", "SubManager");
        var directReportId = await SeedEmployeeAsync(companyId, "Devon", "Report");
        var skipLevelReportId = await SeedEmployeeAsync(companyId, "Dana", "SkipLevel");
        var peerManagerId = await SeedEmployeeAsync(companyId, "Priya", "Peer");
        var unrelatedReportId = await SeedEmployeeAsync(companyId, "Uma", "Unrelated");

        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, peerManagerId, SystemRoles.Manager, companyId);

        var hrBootstrapUserId = Guid.NewGuid();
        using var hrClient = await ClientFor(companyId, hrBootstrapUserId, SystemRoles.HrAdministrator);
        await AssignManagerAsync(hrClient, companyId, subManagerId, managerId);
        await AssignManagerAsync(hrClient, companyId, directReportId, managerId);
        await AssignManagerAsync(hrClient, companyId, skipLevelReportId, subManagerId);
        await AssignManagerAsync(hrClient, companyId, unrelatedReportId, peerManagerId);

        await SeedLeaveRequestAsync(companyId, directReportId, Today.AddDays(5));
        await SeedLeaveRequestAsync(companyId, skipLevelReportId, Today.AddDays(6));
        await SeedLeaveRequestAsync(companyId, unrelatedReportId, Today.AddDays(7));

        using var managerClient = await ClientFor(companyId, managerId, SystemRoles.Manager);
        var payload = await managerClient.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        Assert.NotNull(payload);
        var leave = payload!.Categories.Single(c => c.Category == "Pending Leave Approvals");
        var employeeIds = leave.Items.Select(i => i.EmployeeId).ToList();
        Assert.Contains(directReportId, employeeIds);
        Assert.Contains(skipLevelReportId, employeeIds);
        Assert.DoesNotContain(unrelatedReportId, employeeIds);
        Assert.Equal(2, leave.ActionableCount);
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_DualRole_Caller_Sees_Only_TeamScoped_Items_Not_HrOnly_Or_OutOfHierarchy_Items()
    {
        var companyId = Guid.NewGuid();

        var dualRoleUserId = await SeedEmployeeAsync(companyId, "Dana", "DualRole");
        var directReportId = await SeedEmployeeAsync(companyId, "Devon", "Report");
        var outOfHierarchyEmployeeId = await SeedEmployeeAsync(companyId, "Ola", "Outside");
        var awaitingInvitationEmployeeId = await SeedEmployeeAsync(companyId, "Ines", "Invitee");

        using var hrBootstrapClient = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        await AssignManagerAsync(hrBootstrapClient, companyId, directReportId, dualRoleUserId);
        // outOfHierarchyEmployeeId is deliberately left unassigned — outside the caller's reporting sub-tree.

        await SeedOverdueTaskAsync(companyId, directReportId, Today.AddDays(-1));
        await SeedOverdueTaskAsync(companyId, outOfHierarchyEmployeeId, Today.AddDays(-1));
        await SeedPendingInvitationAsync(companyId, awaitingInvitationEmployeeId);

        using var dualRoleClient = await ClientFor(
            companyId, dualRoleUserId, SystemRoles.Manager, SystemRoles.HrAdministrator);

        var managerPayload = await dualRoleClient.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        Assert.NotNull(managerPayload);
        var managerTasks = managerPayload!.Categories.Single(c => c.Category == "Manager Tasks Overdue");
        var managerTaskEmployeeIds = managerTasks.Items.Select(i => i.EmployeeId).ToList();
        Assert.Contains(directReportId, managerTaskEmployeeIds);
        Assert.DoesNotContain(outOfHierarchyEmployeeId, managerTaskEmployeeIds);
        Assert.Equal(1, managerTasks.ActionableCount);

        var hrOnlyCategory = managerPayload.Categories.SingleOrDefault(c => c.Category == "Employee Accounts Awaiting Invitation");
        Assert.True(hrOnlyCategory is null || hrOnlyCategory.ActionableCount == 0);

        var hrPayload = await dualRoleClient.GetFromJsonAsync<SummaryPayload>(
            $"/api/companies/{companyId}/dashboards/hr/summary");

        Assert.NotNull(hrPayload);
        var hrManagerTasks = hrPayload!.Categories.Single(c => c.Category == "Manager Tasks Overdue");
        var hrManagerTaskEmployeeIds = hrManagerTasks.WaitingItems.Select(i => i.EmployeeId).ToList();
        Assert.Contains(directReportId, hrManagerTaskEmployeeIds);
        Assert.Contains(outOfHierarchyEmployeeId, hrManagerTaskEmployeeIds);
        Assert.Equal(2, hrManagerTasks.WaitingOnOthersCount);
        Assert.Equal(0, hrManagerTasks.ActionableCount);
        Assert.Empty(hrManagerTasks.Items);

        var hrInvitations = hrPayload.Categories.Single(c => c.Category == "Employee Accounts Awaiting Invitation");
        Assert.Contains(hrInvitations.Items, i => i.EmployeeId == awaitingInvitationEmployeeId);
        Assert.Equal(1, hrInvitations.ActionableCount);
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Pending_Leave_For_DirectReport_Is_OwnerActionable()
    {
        var companyId = Guid.NewGuid();
        var managerId = await SeedEmployeeAsync(companyId, "Meera", "Manager");
        var directReportId = await SeedEmployeeAsync(companyId, "Devon", "Report");

        using var hrBootstrapClient = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        await AssignManagerAsync(hrBootstrapClient, companyId, directReportId, managerId);

        await SeedLeaveRequestAsync(companyId, directReportId, Today.AddDays(5));

        using var managerClient = await ClientFor(companyId, managerId, SystemRoles.Manager);
        var payload = await managerClient.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        Assert.NotNull(payload);
        var leave = payload!.Categories.Single(c => c.Category == "Pending Leave Approvals");
        var item = Assert.Single(leave.Items);
        Assert.Equal(directReportId, item.EmployeeId);
        Assert.True(item.IsOwnerActionable);
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_ReturnToWorkReview_For_DirectReport_Now_Appears_As_OwnerActionable()
    {
        var companyId = Guid.NewGuid();
        var managerId = await SeedEmployeeAsync(companyId, "Meera", "Manager");
        var directReportId = await SeedEmployeeAsync(companyId, "Devon", "Report");
        var outOfHierarchyEmployeeId = await SeedEmployeeAsync(companyId, "Ola", "Outside");

        using var hrBootstrapClient = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        await AssignManagerAsync(hrBootstrapClient, companyId, directReportId, managerId);
        // outOfHierarchyEmployeeId is deliberately left unassigned.

        await SeedReturnToWorkReviewAsync(companyId, directReportId, Today.AddDays(3));
        await SeedReturnToWorkReviewAsync(companyId, outOfHierarchyEmployeeId, Today.AddDays(3));

        using var managerClient = await ClientFor(companyId, managerId, SystemRoles.Manager);
        var payload = await managerClient.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        Assert.NotNull(payload);
        var sickness = payload!.Categories.Single(c => c.Category == "Pending Sickness Actions");
        var employeeIds = sickness.Items.Select(i => i.EmployeeId).ToList();

        Assert.Contains(directReportId, employeeIds);
        var ownItem = sickness.Items.Single(i => i.EmployeeId == directReportId);
        Assert.True(ownItem.IsOwnerActionable);

        Assert.DoesNotContain(outOfHierarchyEmployeeId, employeeIds);
    }


    private async Task<Guid> SeedEmployeeAsync(Guid companyId, string firstName, string lastName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName,
            $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
            new DateOnly(2026, 1, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1),
            "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, Now);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private static async Task AssignManagerAsync(HttpClient client, Guid companyId, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/manager",
            new { companyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    private async Task SeedLeaveRequestAsync(Guid companyId, Guid employeeId, DateOnly startDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var request = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
            startDate, LeaveDayPart.FullDay, startDate.AddDays(3), LeaveDayPart.FullDay,
            3m, "Trip", Now);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();
        await SeedLinkedTaskAsync(companyId, request.Id, TaskActionType.Approve);
    }

    private async Task SeedLinkedTaskAsync(Guid companyId, Guid sourceEntityId, TaskActionType actionType)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        db.TaskItems.Add(TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Linked task", null,
            TaskPriority.Medium, TaskSource.Workflow, actionType, Today.AddDays(5),
            null, null, Now, sourceEntityId: sourceEntityId));
        await db.SaveChangesAsync();
    }

    private async Task SeedOverdueTaskAsync(Guid companyId, Guid assignedEmployeeId, DateOnly dueDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        db.TaskItems.Add(TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Complete document check", null,
            TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete, dueDate,
            assignedEmployeeId, null, Now));
        await db.SaveChangesAsync();
    }

    private async Task SeedReturnToWorkReviewAsync(Guid companyId, Guid employeeId, DateOnly dueDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SicknessDbContext>();
        var categoryId = Guid.NewGuid();
        db.SicknessCategories.Add(SicknessCategory.Create(categoryId, companyId, $"Illness-{categoryId:N}", 1, Now));
        var record = SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, categoryId,
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay, null, null, null, null,
            SicknessEvidenceStatus.NotRequired, Now);
        db.SicknessRecords.Add(record);
        var review = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, record.Id, employeeId, dueDate, Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();
        await SeedLinkedTaskAsync(companyId, review.Id, TaskActionType.Review);
    }

    private async Task SeedPendingInvitationAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.UserInvites.Add(UserInvite.Create(employeeId, companyId, $"{employeeId:N}@example.com", Now));
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
        List<ActionItemPayload>? WaitingItems = null,
        int WaitingOnOthersCount = 0)
    {
        public List<ActionItemPayload> WaitingItems { get; init; } = WaitingItems ?? [];
    }

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
        Guid? TaskId,
        bool IsOwnerActionable = true,
        string? OwnerLabel = null);
}
