using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies the cross-module side effect: submitting a leave request creates
/// a task (and notification) for the employee's manager via the in-process
/// LeaveRequestedIntegrationEvent → LeaveRequestedHandler pipeline.
/// </summary>
public class LeaveSubmittedCreatesTaskTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser          = Guid.Parse("11100001-0000-0000-0000-000000000001");
    private static readonly Guid SeededCompanyId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnnualLeaveTypeId  = Guid.Parse("A0000000-0000-0000-0000-000000000001");

    public LeaveSubmittedCreatesTaskTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Submit_Leave_Creates_Task_Assigned_To_Manager()
    {
        var (client, policyId, manager, report) = await SetupAsync();

        await AssignPolicyAsync(client, report.Id, policyId);
        await SubmitLeaveAsync(client, report.Id);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{manager.Id}/tasks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<TaskListPayload>();
        var task    = Assert.Single(payload!.Items.Where(t => t.Source == "Leave"));
        Assert.Contains(report.FirstName, task.Title);
        Assert.Equal("Medium",            task.Priority);
        Assert.Equal(manager.Id,          task.AssignedEmployeeId);
    }

    [Fact]
    public async Task Submit_Leave_Creates_Unread_Notification_For_Manager()
    {
        var (client, policyId, manager, report) = await SetupAsync();

        await AssignPolicyAsync(client, report.Id, policyId);
        await SubmitLeaveAsync(client, report.Id);

        using var managerClient = AsEmployee(manager.Id);
        var response = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/notifications/my");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<NotifListPayload>();
        Assert.True(payload!.UnreadCount >= 1);
        Assert.Contains(payload.Items, n => !n.IsRead && n.Type == "TaskAssigned");
    }

    [Fact]
    public async Task Submit_Leave_Without_Manager_Does_Not_Fail()
    {
        using var client   = AdminClient();
        var policyId       = await CreatePolicyAsync(client);
        var orphan         = await CreateEmployeeAsync(client, "Orphan", "Employee");
        await AssignPolicyAsync(client, orphan.Id, policyId);

        // No manager assigned — handler should still succeed (task goes unassigned)
        var response = await SubmitLeaveAsync(client, orphan.Id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, Guid PolicyId, EmpPayload Manager, EmpPayload Report)> SetupAsync()
    {
        var client   = AdminClient();
        var policyId = await CreatePolicyAsync(client);
        var manager  = await CreateEmployeeAsync(client, "Leave", "Manager");
        var report   = await CreateEmployeeAsync(client, "Leave", "Report");

        // Assign manager to the report
        var assignResp = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{report.Id}/manager",
            new { companyId = SeededCompanyId, id = report.Id, managerId = manager.Id });
        assignResp.EnsureSuccessStatusCode();

        return (client, policyId, manager, report);
    }

    private async Task<Guid> CreatePolicyAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies",
            new { companyId = SeededCompanyId, name = $"Test Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = true });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<PolicyPayload>();
        return payload!.Id;
    }

    private async Task<EmpPayload> CreateEmployeeAsync(HttpClient client, string firstName, string lastName)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId   = SeededCompanyId,
                firstName,
                lastName,
                workEmail   = $"{firstName.ToLower()}.{lastName.ToLower()}.{Guid.NewGuid():N}@test.com",
                startDate   = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender      = "Male"
            });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<EmpPayload>())!;
    }

    private async Task AssignPolicyAsync(HttpClient client, Guid employeeId, Guid policyId)
    {
        var resp = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/leave-policy",
            new { companyId = SeededCompanyId, employeeId, leavePolicyId = policyId, effectiveFrom = "2026-01-01" });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> SubmitLeaveAsync(HttpClient client, Guid employeeId)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/leave-requests",
            new
            {
                companyId   = SeededCompanyId,
                employeeId,
                leaveTypeId = AnnualLeaveTypeId,
                startDate   = "2027-08-02",
                startPart   = "FullDay",
                endDate     = "2027-08-06",
                endPart     = "FullDay",
                reason      = "Cross-module test"
            });
        return resp;
    }

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private HttpClient AsEmployee(Guid employeeId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private sealed record PolicyPayload(Guid Id);
    private sealed record EmpPayload(Guid Id, string FirstName, string LastName);
    private sealed record TaskListPayload(IReadOnlyList<TaskItem> Items);
    private sealed record TaskItem(Guid Id, string Title, string Source, string Priority, Guid? AssignedEmployeeId);
    private sealed record NotifListPayload(int UnreadCount, IReadOnlyList<NotifItem> Items);
    private sealed record NotifItem(Guid Id, string Title, bool IsRead, string Type);
}
