using HR.Infrastructure.Abstractions;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class HrDashboardActionabilityEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.Date);

    public HrDashboardActionabilityEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/dashboards/hr/summary";

    private async Task<HttpClient> HrClientAsync(Guid companyId, Guid userId, bool alsoManager = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        if (alsoManager)
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager, companyId);
        return client;
    }

    [Fact]
    public async Task HrOwnedUnassignedOnboardingTask_IsInNeedsYourAction()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Nadia", "Newstarter");
        var onboardingTaskId = await SeedOnboardingTaskAsync(companyId, employeeId);
        await SeedLinkedTaskAsync(companyId, onboardingTaskId, TaskSource.Onboarding, TaskActionType.Complete, assignee: null);

        using var client = await HrClientAsync(companyId, Guid.NewGuid());
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        var category = Assert.Single(payload!.Categories, c => c.Category == "Outstanding Onboarding Tasks");
        var item = Assert.Single(category.Items);
        Assert.Equal("CanAct", item.Actionability);
        Assert.NotNull(item.TaskId);
        Assert.Equal(1, category.ActionableCount);
        Assert.Equal(0, category.WaitingOnOthersCount);
    }

    [Fact]
    public async Task ManagerOwnedLeaveApproval_IsWaitingOnOthers_WithoutAnActionableDestination()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Layla", "Leaver");
        var managerId = await SeedEmployeeAsync(companyId, "Mo", "Manager");
        var requestId = await SeedLeaveRequestAsync(companyId, employeeId);
        await SeedLinkedTaskAsync(companyId, requestId, TaskSource.Workflow, TaskActionType.Approve, assignee: managerId);

        using var client = await HrClientAsync(companyId, Guid.NewGuid());
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        var category = Assert.Single(payload!.Categories, c => c.Category == "Pending Leave Approvals");
        Assert.Empty(category.Items);
        var item = Assert.Single(category.WaitingItems);
        Assert.Equal("VisibilityOnly", item.Actionability);
        Assert.Null(item.TaskId);
        Assert.Equal("", item.DeepLinkUrl);
        Assert.Equal("Assigned to Mo Manager", item.OwnerLabel);
        Assert.Equal($"/companies/{companyId}/employees/{employeeId}", item.MonitoringUrl);
        Assert.Equal(0, payload.TotalActionableCount);
        Assert.Equal(1, payload.TotalWaitingOnOthersCount);
    }

    [Fact]
    public async Task EmployeeOwnedSicknessEvidenceRequest_IsWaitingOnOthers_NamingTheEmployee()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Ellis", "Evidence");
        await SeedSicknessEvidenceRequestAsync(companyId, employeeId, withRecord: true);

        using var client = await HrClientAsync(companyId, Guid.NewGuid());
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        var category = Assert.Single(payload!.Categories, c => c.Category == "Pending Sickness Actions");
        var item = Assert.Single(category.WaitingItems);
        Assert.Equal("Owned by the employee", item.OwnerLabel);
        Assert.Equal(0, category.ActionableCount);
    }

    [Fact]
    public async Task OutstandingOnboardingItemWithoutAnOpenTask_BecomesAnExplicitException_NotADeadEnd()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Orla", "Orphan");
        await SeedOnboardingTaskAsync(companyId, employeeId);

        using var client = await HrClientAsync(companyId, Guid.NewGuid());
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        var category = Assert.Single(payload!.Categories, c => c.Category == "Outstanding Onboarding Tasks");
        Assert.Empty(category.Items);
        Assert.Empty(category.WaitingItems);
        Assert.Equal(1, category.UnavailableCount);
        Assert.Equal(1, payload.TotalUnavailableCount);
        var exception = Assert.Single(payload.Exceptions);
        Assert.Equal("Outstanding Onboarding Tasks", exception.Category);
        Assert.Equal($"/companies/{companyId}/employees/{employeeId}", exception.InvestigationUrl);
        Assert.Contains("administrator investigation", exception.Message);
    }

    [Fact]
    public async Task HrAndManager_DirectReportLeaveWithoutTask_IsActionableOnceInBothDashboards()
    {
        var companyId = Guid.NewGuid();
        var hrManagerId = await SeedEmployeeAsync(companyId, "David", "Park");
        var reportId = await SeedEmployeeAsync(companyId, "Emma", "Jones");
        await SeedLeaveRequestAsync(companyId, reportId);

        using var client = await HrClientAsync(companyId, hrManagerId, alsoManager: true);
        var assign = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{reportId}/manager",
            new { companyId, id = reportId, managerId = hrManagerId });
        assign.EnsureSuccessStatusCode();

        var hr = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));
        var manager = await client.GetFromJsonAsync<SummaryPayload>(
            $"/api/companies/{companyId}/dashboards/manager/summary");

        var hrCategory = Assert.Single(hr!.Categories, c => c.Category == "Pending Leave Approvals");
        var managerCategory = Assert.Single(manager!.Categories, c => c.Category == "Pending Leave Approvals");

        Assert.Equal(reportId, Assert.Single(hrCategory.Items).EmployeeId);
        Assert.Empty(hrCategory.WaitingItems);
        Assert.Equal(1, hrCategory.ActionableCount);
        Assert.Equal(reportId, Assert.Single(managerCategory.Items).EmployeeId);
        Assert.Equal(1, managerCategory.ActionableCount);
        Assert.Equal(0, managerCategory.UnavailableCount);
        Assert.Contains("tab=leave", managerCategory.Items[0].DeepLinkUrl);
    }

    [Fact]
    public async Task UserWithBothHrAndManagerRoles_CanActOnTheirOwnApprovals_ButOnlyMonitorOthers()
    {
        var companyId = Guid.NewGuid();
        var hrManagerId = await SeedEmployeeAsync(companyId, "Hana", "HrManager");
        var reportId = await SeedEmployeeAsync(companyId, "Rory", "Report");
        var otherEmployeeId = await SeedEmployeeAsync(companyId, "Olu", "Other");
        var otherManagerId = await SeedEmployeeAsync(companyId, "Pat", "OtherManager");

        var mineRequest = await SeedLeaveRequestAsync(companyId, reportId);
        var theirsRequest = await SeedLeaveRequestAsync(companyId, otherEmployeeId);
        await SeedLinkedTaskAsync(companyId, mineRequest, TaskSource.Workflow, TaskActionType.Approve, assignee: hrManagerId);
        await SeedLinkedTaskAsync(companyId, theirsRequest, TaskSource.Workflow, TaskActionType.Approve, assignee: otherManagerId);

        using var client = await HrClientAsync(companyId, hrManagerId, alsoManager: true);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        var category = Assert.Single(payload!.Categories, c => c.Category == "Pending Leave Approvals");
        Assert.Equal(reportId, Assert.Single(category.Items).EmployeeId);
        Assert.Equal(otherEmployeeId, Assert.Single(category.WaitingItems).EmployeeId);
    }

    [Fact]
    public async Task OverdueManagerTask_StaysVisibilityOnly_AndKeepsOverdueStyling()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Tess", "Tardy");
        await SeedOverdueTaskAsync(companyId, employeeId);

        using var client = await HrClientAsync(companyId, Guid.NewGuid());
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        var category = Assert.Single(payload!.Categories, c => c.Category == "Manager Tasks Overdue");
        var item = Assert.Single(category.WaitingItems);
        Assert.True(item.IsOverdue);
        Assert.Equal("Overdue", item.Urgency);
        Assert.Empty(category.Items);
    }

    [Fact]
    public async Task ReassigningATaskToTheHrQueue_MakesItActionable()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Eve", "Escalated");
        var managerId = await SeedEmployeeAsync(companyId, "Max", "Manager");
        var onboardingTaskId = await SeedOnboardingTaskAsync(companyId, employeeId);
        var linkedTaskId = await SeedLinkedTaskAsync(companyId, onboardingTaskId, TaskSource.Onboarding, TaskActionType.Complete, assignee: managerId);

        using var client = await HrClientAsync(companyId, Guid.NewGuid());
        var before = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));
        var beforeCategory = Assert.Single(before!.Categories, c => c.Category == "Outstanding Onboarding Tasks");
        Assert.Empty(beforeCategory.Items);
        Assert.Single(beforeCategory.WaitingItems);

        await ReassignTaskAsync(linkedTaskId, assignee: null);

        var after = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));
        var afterCategory = Assert.Single(after!.Categories, c => c.Category == "Outstanding Onboarding Tasks");
        Assert.Single(afterCategory.Items);
        Assert.Empty(afterCategory.WaitingItems);
    }

    [Fact]
    public async Task CompletedTasks_AreInNeitherQueue()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Cal", "Completed");
        await SeedOverdueTaskAsync(companyId, employeeId, complete: true);

        using var client = await HrClientAsync(companyId, Guid.NewGuid());
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        var category = Assert.Single(payload!.Categories, c => c.Category == "Manager Tasks Overdue");
        Assert.Empty(category.Items);
        Assert.Empty(category.WaitingItems);
        Assert.Equal(0, category.UnavailableCount);
    }

    [Fact]
    public async Task ActionabilityIsComputedOnTheServer_ClientSuppliedFlagsChangeNothing()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Layla", "Leaver");
        var managerId = await SeedEmployeeAsync(companyId, "Mo", "Manager");
        var requestId = await SeedLeaveRequestAsync(companyId, employeeId);
        await SeedLinkedTaskAsync(companyId, requestId, TaskSource.Workflow, TaskActionType.Approve, assignee: managerId);

        using var client = await HrClientAsync(companyId, Guid.NewGuid());
        var plain = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));
        var manipulated = await client.GetFromJsonAsync<SummaryPayload>(
            Url(companyId) + "?isOwnerActionable=true&actionability=CanAct&scope=Manager&assignedToMe=true");

        Assert.Equal(plain!.TotalActionableCount, manipulated!.TotalActionableCount);
        Assert.Equal(plain.TotalWaitingOnOthersCount, manipulated.TotalWaitingOnOthersCount);
        Assert.Equal(0, manipulated.TotalActionableCount);
        Assert.Equal(1, manipulated.TotalWaitingOnOthersCount);
    }

    [Fact]
    public async Task Counts_AreSeparate_AndTotalsEqualTheSumOfCategories()
    {
        var companyId = Guid.NewGuid();
        var a = await SeedEmployeeAsync(companyId, "Ann", "A");
        var b = await SeedEmployeeAsync(companyId, "Bob", "B");
        var manager = await SeedEmployeeAsync(companyId, "Cam", "Manager");

        var onboardingTask = await SeedOnboardingTaskAsync(companyId, a);
        await SeedLinkedTaskAsync(companyId, onboardingTask, TaskSource.Onboarding, TaskActionType.Complete, assignee: null);
        var leaveRequest = await SeedLeaveRequestAsync(companyId, b);
        await SeedLinkedTaskAsync(companyId, leaveRequest, TaskSource.Workflow, TaskActionType.Approve, assignee: manager);
        await SeedOverdueTaskAsync(companyId, b);

        using var client = await HrClientAsync(companyId, Guid.NewGuid());
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        Assert.Equal(payload!.Categories.Sum(c => c.ActionableCount), payload.TotalActionableCount);
        Assert.Equal(payload.Categories.Sum(c => c.WaitingOnOthersCount), payload.TotalWaitingOnOthersCount);
        Assert.Equal(1, payload.TotalActionableCount);
        Assert.Equal(2, payload.TotalWaitingOnOthersCount);
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

    private async Task<Guid> SeedLeaveRequestAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var request = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
            Today.AddDays(5), LeaveDayPart.FullDay, Today.AddDays(8), LeaveDayPart.FullDay,
            3m, "Trip", Now);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task<Guid> SeedOnboardingTaskAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
        var plan = OnboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, Today, null, Now);
        db.OnboardingPlans.Add(plan);
        var task = OnboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Set up laptop", null,
            OnboardingTemplateTaskAssignTo.Manager, Today.AddDays(5), Now);
        db.OnboardingTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task<Guid> SeedLinkedTaskAsync(
        Guid companyId, Guid sourceEntityId, TaskSource source, TaskActionType actionType, Guid? assignee)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Linked task", null,
            TaskPriority.Medium, source, actionType, Today.AddDays(5),
            assignee, null, Now, sourceEntityId: sourceEntityId);
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task ReassignTaskAsync(Guid taskId, Guid? assignee)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var task = await db.TaskItems.SingleAsync(t => t.Id == taskId);
        task.Reassign(assignee, null, Now);
        await db.SaveChangesAsync();
    }

    private async Task SeedOverdueTaskAsync(Guid companyId, Guid assignedEmployeeId, bool complete = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Complete document check", null,
            TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete, Today.AddDays(-4),
            assignedEmployeeId, null, Now);
        if (complete)
            task.Complete(Guid.NewGuid(), Now);
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();
    }

    private async Task SeedSicknessEvidenceRequestAsync(Guid companyId, Guid employeeId, bool withRecord)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SicknessDbContext>();
        var recordId = Guid.NewGuid();
        if (withRecord)
        {
            var categoryId = Guid.NewGuid();
            db.SicknessCategories.Add(SicknessCategory.Create(categoryId, companyId, $"Illness-{categoryId:N}", 1, Now));
            var record = SicknessRecord.Create(
                recordId, companyId, employeeId, categoryId,
                new DateOnly(2026, 7, 1), SicknessDayPart.FullDay, null, null, null, null,
                SicknessEvidenceStatus.NotRequired, Now);
            db.SicknessRecords.Add(record);
        }

        db.SicknessEvidenceRequests.Add(SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, recordId, Guid.NewGuid(), Today.AddDays(-2), null, Now));
        await db.SaveChangesAsync();
    }

    private sealed record SummaryPayload(
        List<CategoryPayload> Categories,
        int TotalActionableCount,
        int TotalWaitingOnOthersCount,
        int TotalUnavailableCount,
        List<ExceptionPayload> Exceptions);

    private sealed record ExceptionPayload(string Category, string Message, string? EmployeeName, string? InvestigationUrl);

    private sealed record CategoryPayload(
        string Category,
        int ActionableCount,
        int WaitingOnOthersCount,
        int UnavailableCount,
        List<ItemPayload> Items,
        List<ItemPayload> WaitingItems);

    private sealed record ItemPayload(
        Guid? EmployeeId,
        string EmployeeName,
        string ActionType,
        string Urgency,
        bool IsOverdue,
        string DeepLinkUrl,
        Guid? TaskId,
        string? OwnerLabel,
        string Actionability,
        string? VisibilityReason,
        string? MonitoringUrl);
}
