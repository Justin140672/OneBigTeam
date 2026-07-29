using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report integration coverage. Every <see cref="HR.Infrastructure.Abstractions.IWorkloadActionProvider"/>
/// self-scopes by caller (see xmldoc on that interface), so the key security assertion here is that
/// no persona ever receives another manager's or another category's company-wide data — the
/// aggregation endpoint's baseline "reporting:view" policy is only a menu gate.
/// </summary>
public class GetWorkloadActionsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.Date);

    public GetWorkloadActionsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_WorkloadActions_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/workload-actions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WorkloadActions_Returns_Forbidden_For_Caller_With_No_Baseline_Reporting_Role()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = ClientFor(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/workload-actions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_WorkloadActions_HrAdministrator_Sees_Items_Spanning_Multiple_Categories()
    {
        var companyId = Guid.NewGuid();
        var hrAdminId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrAdminId, SystemRoles.HrAdministrator);
        // VacanciesAwaitingActionWorkloadActionProvider is Recruiter-scoped only (see its xmldoc),
        // so this persona also needs the Recruiter role to exercise that category alongside the
        // HR-only/Manager-or-HR categories below.
        await TestRoleSeeder.AssignRoleAsync(_factory, hrAdminId, SystemRoles.Recruiter);
        using var hrClient = ClientFor(companyId, hrAdminId);

        var employeeId = await SeedEmployeeAsync(companyId, "Priya", "Patel");

        // Pending leave approval.
        await SeedLeaveRequestAsync(companyId, employeeId, Today.AddDays(5));

        // Due probation review (via the real HTTP endpoints, matching CompleteProbationReviewEndpointTests).
        await SeedProbationRecordAndReviewAsync(hrClient, companyId, employeeId, Today.AddDays(5));

        // Overdue task assigned to the same employee.
        await SeedOverdueTaskAsync(companyId, employeeId, Today.AddDays(-2));

        // Open vacancy with no recruiter assigned.
        await SeedOpenVacancyAsync(companyId, hiringManagerId: employeeId, assignedRecruiterId: null);

        var response = await hrClient.GetAsync($"/api/companies/{companyId}/reporting/workload-actions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WorkloadActionsPayload>();
        Assert.NotNull(payload);

        var categories = payload!.Items.Select(i => i.ActionCategory).Distinct().ToList();
        Assert.Contains("Pending Leave Approvals", categories);
        Assert.Contains("Probation Reviews Due", categories);
        Assert.Contains("Manager Tasks Overdue", categories);
        Assert.Contains("Vacancies Awaiting Action", categories);
        Assert.True(payload.Summary.TotalOutstanding >= 4);
    }

    [Fact]
    public async Task Get_WorkloadActions_Manager_Only_Sees_Own_DirectReports_Items_Never_HrOnly_Categories()
    {
        var companyId = Guid.NewGuid();
        var managerId = await SeedEmployeeAsync(companyId, "Meera", "Manager");
        var directReportId = await SeedEmployeeAsync(companyId, "Devon", "Report");
        var otherManagerId = await SeedEmployeeAsync(companyId, "Oscar", "OtherManager");
        var otherManagersReportId = await SeedEmployeeAsync(companyId, "Nina", "OtherReport");

        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);
        await TestRoleSeeder.AssignRoleAsync(_factory, otherManagerId, SystemRoles.Manager);

        // "employee:manage" (AssignManager's policy) is HR-only, so a dedicated HR Administrator
        // seeds the reporting-line data — the Manager persona under test never needs that policy.
        var hrBootstrapUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrBootstrapUserId, SystemRoles.HrAdministrator);
        using var hrBootstrapClient = ClientFor(companyId, hrBootstrapUserId);
        await AssignManagerAsync(hrBootstrapClient, companyId, directReportId, managerId);
        await AssignManagerAsync(hrBootstrapClient, companyId, otherManagersReportId, otherManagerId);

        // Own direct report's pending leave request — should be visible.
        await SeedLeaveRequestAsync(companyId, directReportId, Today.AddDays(5));
        // Another manager's direct report's pending leave request — must never be visible.
        await SeedLeaveRequestAsync(companyId, otherManagersReportId, Today.AddDays(5));

        // Overdue task for own direct report — should be visible via Manager Tasks Overdue.
        await SeedOverdueTaskAsync(companyId, directReportId, Today.AddDays(-1));
        // Overdue task for the other manager's report — must never be visible.
        await SeedOverdueTaskAsync(companyId, otherManagersReportId, Today.AddDays(-1));

        using var client = ClientFor(companyId, managerId);
        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/workload-actions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WorkloadActionsPayload>();
        Assert.NotNull(payload);

        Assert.All(payload!.Items, item => Assert.NotEqual(otherManagersReportId, item.EmployeeId));
        Assert.Contains(payload.Items, i => i.EmployeeId == directReportId);

        // Sickness is HR-only — a Manager, regardless of direct reports, must never see it.
        Assert.DoesNotContain(payload.Items, i => i.ActionCategory == "Pending Sickness Actions");
        // Recruitment is Recruiter-only — a plain Manager must never see it either.
        Assert.DoesNotContain(payload.Items, i => i.ActionCategory == "Vacancies Awaiting Action");
    }

    [Fact]
    public async Task Get_WorkloadActions_Recruiter_Only_Sees_Recruitment_Category_Items()
    {
        var companyId = Guid.NewGuid();
        var recruiterId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, recruiterId, SystemRoles.Recruiter);

        var hiringManagerId = await SeedEmployeeAsync(companyId, "Harriet", "Manager");
        var otherEmployeeId = await SeedEmployeeAsync(companyId, "Owen", "Employee");

        await SeedOpenVacancyAsync(companyId, hiringManagerId, assignedRecruiterId: null);
        // Leave/tasks belonging to someone else — a Recruiter must never see these HR/Manager
        // scoped categories, even though the endpoint's baseline policy lets them through the gate.
        await SeedLeaveRequestAsync(companyId, otherEmployeeId, Today.AddDays(5));
        await SeedOverdueTaskAsync(companyId, otherEmployeeId, Today.AddDays(-1));

        using var client = ClientFor(companyId, recruiterId);
        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/workload-actions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WorkloadActionsPayload>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, item => Assert.Equal("Vacancies Awaiting Action", item.ActionCategory));
    }

    [Fact]
    public async Task Get_WorkloadActions_Filters_By_Urgency_Overdue_EndToEnd()
    {
        var companyId = Guid.NewGuid();
        var hrAdminId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrAdminId, SystemRoles.HrAdministrator);
        using var client = ClientFor(companyId, hrAdminId);

        var employeeId = await SeedEmployeeAsync(companyId, "Farah", "Overdue");
        var otherEmployeeId = await SeedEmployeeAsync(companyId, "Upcoming", "Task");

        await SeedOverdueTaskAsync(companyId, employeeId, Today.AddDays(-5));
        await SeedOverdueTaskAsync(companyId, otherEmployeeId, Today.AddDays(30), status: TaskItemStatus.Open); // not overdue, filtered out by provider anyway

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/workload-actions?urgency=Overdue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WorkloadActionsPayload>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, item => Assert.Equal("Overdue", item.Urgency));
    }

    [Fact]
    public async Task Get_WorkloadActions_Filters_By_ActionType_Substring_EndToEnd()
    {
        var companyId = Guid.NewGuid();
        var hrAdminId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrAdminId, SystemRoles.HrAdministrator);
        using var client = ClientFor(companyId, hrAdminId);

        var employeeId = await SeedEmployeeAsync(companyId, "Leave", "Requester");
        await SeedLeaveRequestAsync(companyId, employeeId, Today.AddDays(5));
        await SeedOverdueTaskAsync(companyId, employeeId, Today.AddDays(-2));

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/workload-actions?actionType=Approve Leave Request");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WorkloadActionsPayload>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, item => Assert.Equal("Approve Leave Request", item.ActionType));
    }

    [Fact]
    public async Task Get_WorkloadActions_HrAdministrator_Sees_Outstanding_Onboarding_Tasks()
    {
        var companyId = Guid.NewGuid();
        var hrAdminId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrAdminId, SystemRoles.HrAdministrator);
        using var client = ClientFor(companyId, hrAdminId);

        var employeeId = await SeedEmployeeAsync(companyId, "Nadia", "Newstarter");
        await SeedOutstandingOnboardingTaskAsync(companyId, employeeId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/workload-actions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WorkloadActionsPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.ActionCategory == "Outstanding Onboarding Tasks" && i.EmployeeId == employeeId);
    }

    [Fact]
    public async Task Get_WorkloadActions_UnauthorizedCaller_Never_Sees_Outstanding_Onboarding_Tasks()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Nadia", "Newstarter");
        await SeedOutstandingOnboardingTaskAsync(companyId, employeeId);

        // A caller with no reporting-related role at all is forbidden at the endpoint's baseline
        // reporting:view gate before any provider even runs.
        var callerId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, callerId, SystemRoles.Employee);
        using var client = ClientFor(companyId, callerId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/workload-actions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_WorkloadActions_HrAdministrator_Sees_Employee_Accounts_Awaiting_Invitation()
    {
        var companyId = Guid.NewGuid();
        var hrAdminId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrAdminId, SystemRoles.HrAdministrator);
        using var client = ClientFor(companyId, hrAdminId);

        var employeeId = await SeedEmployeeAsync(companyId, "Ivan", "Invited");
        await SeedUserInviteAsync(companyId, employeeId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/workload-actions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WorkloadActionsPayload>();
        Assert.NotNull(payload);
        Assert.Contains(
            payload!.Items,
            i => i.ActionCategory == "Employee Accounts Awaiting Invitation" && i.EmployeeId == employeeId);
    }

    [Fact]
    public async Task Get_WorkloadActions_Manager_Never_Sees_Employee_Accounts_Awaiting_Invitation()
    {
        var companyId = Guid.NewGuid();
        var managerId = await SeedEmployeeAsync(companyId, "Meera", "Manager");
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        var invitedEmployeeId = await SeedEmployeeAsync(companyId, "Ivan", "Invited");
        await SeedUserInviteAsync(companyId, invitedEmployeeId);

        using var client = ClientFor(companyId, managerId);
        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/workload-actions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WorkloadActionsPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.ActionCategory == "Employee Accounts Awaiting Invitation");
    }

    // ── Seeding helpers ──────────────────────────────────────────────────────

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

    private async Task<Guid> SeedLeaveRequestAsync(Guid companyId, Guid employeeId, DateOnly startDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var request = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
            startDate, LeaveDayPart.FullDay, startDate.AddDays(3), LeaveDayPart.FullDay,
            3m, "Trip", Now);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private static async Task SeedProbationRecordAndReviewAsync(
        HttpClient hrClient, Guid companyId, Guid employeeId, DateOnly dueDate)
    {
        var recordResponse = await hrClient.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId,
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-01-01",
            expectedEndDate = "2026-10-01"
        });
        recordResponse.EnsureSuccessStatusCode();
        var record = await recordResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        var reviewResponse = await hrClient.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record!.Id,
            reviewType = "ManagerCheckIn",
            dueDate = dueDate.ToString("yyyy-MM-dd")
        });
        reviewResponse.EnsureSuccessStatusCode();
    }

    private async Task<Guid> SeedOverdueTaskAsync(
        Guid companyId, Guid assignedEmployeeId, DateOnly dueDate, TaskItemStatus status = TaskItemStatus.Open)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Complete document check", null,
            TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete, dueDate,
            assignedEmployeeId, null, Now);
        if (status == TaskItemStatus.InProgress) task.Start(Now);
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task<Guid> SeedOpenVacancyAsync(Guid companyId, Guid hiringManagerId, Guid? assignedRecruiterId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Vacancy", null, hiringManagerId, Now, assignedRecruiterId);
        vacancy.Open(Now, Today);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    private async Task SeedOutstandingOnboardingTaskAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
        var plan = OnboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, Today, null, Now);
        db.OnboardingPlans.Add(plan);
        db.OnboardingTasks.Add(OnboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Set up laptop", null,
            OnboardingTemplateTaskAssignTo.Manager, Today.AddDays(5), Now));
        await db.SaveChangesAsync();
    }

    private async Task SeedUserInviteAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.UserInvites.Add(UserInvite.Create(employeeId, companyId, $"invite.{Guid.NewGuid():N}@example.com", Now));
        await db.SaveChangesAsync();
    }

    private sealed record WorkloadActionsPayload(
        List<WorkloadActionItemPayload> Items,
        List<object> Groups,
        WorkloadActionSummaryPayload Summary);

    private sealed record WorkloadActionItemPayload(
        Guid EmployeeId,
        string EmployeeName,
        string? Department,
        string ActionType,
        string ActionCategory,
        DateOnly? DueDate,
        string? AssignedTo,
        string Status,
        string Urgency,
        string DeepLinkUrl);

    private sealed record WorkloadActionSummaryPayload(int TotalOutstanding, int Overdue, int DueToday, int DueThisWeek);

    private sealed record ProbationRecordPayload(Guid Id, Guid CompanyId);
}
