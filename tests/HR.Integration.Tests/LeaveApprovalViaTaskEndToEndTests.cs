using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies the complete leave-via-task lifecycle as a single coherent flow:
///
///   Create employee + manager → assign manager relationship → assign leave policy
///     → Seed balance
///     → Submit leave request → task auto-created for manager (Source=Leave, ActionType=Approve)
///     → Manager completes task with outcomeDecision=Approve
///     → Leave request status transitions to Approved
///     → Balance is deducted (UsedDays > 0)
///
/// This covers the cross-module handshake that is NOT exercised by LeaveLifecycleIntegrationTests
/// (which uses the direct /approve endpoint) or LeaveSubmittedCreatesTaskTests (which only
/// checks that the task was created, not that completing it approves the leave).
/// </summary>
[Collection("Integration")]
public class LeaveApprovalViaTaskEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser = new("c1c1c1c1-0000-0000-0000-000000000001");

    public LeaveApprovalViaTaskEndToEndTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Full_Leave_Lifecycle_Submit_Via_Task_Approval_Deducts_Balance()
    {
        var companyId  = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Employee);

        using var adminClient   = AuthenticatedClient(AdminUser, companyId);
        using var managerClient = AuthenticatedClient(managerId, companyId);

        // ── Step 1: Create leave type ─────────────────────────────────────────
        var leaveTypeId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            db.LeaveTypes.Add(LeaveType.Create(
                leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
                AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        // ── Step 2: Create leave policy ───────────────────────────────────────
        var policyResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<IdPayload>();

        // ── Step 3: Create manager and employee ───────────────────────────────
        var refData      = await CreateReferenceDataAsync(adminClient, companyId);
        var managerEmpId = await CreateEmployeeAsync(adminClient, companyId, "Leave", "Manager", refData);
        var empId        = await CreateEmployeeAsync(adminClient, companyId, "Leave", "Employee", refData);

        // ── Step 4: Assign manager relationship ───────────────────────────────
        var managerAssignResp = await adminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{empId}/manager",
            new { companyId, id = empId, managerId = managerEmpId });
        managerAssignResp.EnsureSuccessStatusCode();

        // ── Step 5: Assign leave policy to employee ───────────────────────────
        var policyAssignResp = await adminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{empId}/leave-policy",
            new { companyId, employeeId = empId, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        policyAssignResp.EnsureSuccessStatusCode();

        // ── Step 6: Ensure leave balance exists ───────────────────────────────
        await EnsureBalanceAsync(companyId, empId, leaveTypeId, policy.Id);

        // ── Step 7: Submit leave request ──────────────────────────────────────
        // Mon–Fri = 5 working days
        var submitResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{empId}/leave-requests",
            new
            {
                companyId,
                employeeId = empId,
                leaveTypeId,
                startDate = "2026-08-03",
                startPart = "FullDay",
                endDate   = "2026-08-07",
                endPart   = "FullDay",
                reason    = "Lifecycle test"
            });
        submitResp.EnsureSuccessStatusCode();
        var leaveRequest = await submitResp.Content.ReadFromJsonAsync<LeaveRequestPayload>();
        Assert.Equal("Pending", leaveRequest!.Status);

        // ── Step 8: Verify task was created for manager ───────────────────────
        var managerTasks = await GetEmployeeTasksAsync(adminClient, companyId, managerEmpId);
        var approvalTask = Assert.Single(
            managerTasks,
            t => t.Source == "Leave" && t.ActionType == "Approve" && t.SourceEntityId == leaveRequest.Id);
        Assert.Equal("Open", approvalTask.Status);

        // ── Step 9: Manager completes the task with Approve decision ──────────
        var completeResp = await managerClient.PostAsync(
            $"/api/companies/{companyId}/tasks/{approvalTask.Id}/complete",
            Json(new { outcomeDecision = "Approve" }));
        completeResp.EnsureSuccessStatusCode();

        // ── Step 10: Leave request should now be Approved ─────────────────────
        var listResp = await adminClient.GetAsync(
            $"/api/companies/{companyId}/employees/{empId}/leave-requests");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<LeaveListPayload>();
        var request = Assert.Single(list!.Items);
        Assert.Equal("Approved", request.Status);

        // ── Step 11: Balance should reflect the 5-day deduction ───────────────
        var balanceResp = await adminClient.GetAsync(
            $"/api/companies/{companyId}/employees/{empId}/leave-balances?policyYear={DateTimeOffset.UtcNow.Year}");
        balanceResp.EnsureSuccessStatusCode();
        var balancePayload = await balanceResp.Content.ReadFromJsonAsync<BalanceResponse>();
        var balance = balancePayload!.Balances.Single(b => b.LeaveTypeId == leaveTypeId);
        Assert.Equal(5m, balance.UsedDays);
        Assert.Equal(20m, balance.RemainingDays);
    }

    [Fact]
    public async Task Full_Leave_Lifecycle_Submit_Via_Task_Rejection_Does_Not_Deduct_Balance()
    {
        var companyId  = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Employee);

        using var adminClient   = AuthenticatedClient(AdminUser, companyId);
        using var managerClient = AuthenticatedClient(managerId, companyId);

        var leaveTypeId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            db.LeaveTypes.Add(LeaveType.Create(
                leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
                AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var policyResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<IdPayload>();

        var refData2     = await CreateReferenceDataAsync(adminClient, companyId);
        var managerEmpId = await CreateEmployeeAsync(adminClient, companyId, "Leave", "Manager", refData2);
        var empId        = await CreateEmployeeAsync(adminClient, companyId, "Leave", "Employee", refData2);

        var managerAssignResp = await adminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{empId}/manager",
            new { companyId, id = empId, managerId = managerEmpId });
        managerAssignResp.EnsureSuccessStatusCode();

        var policyAssignResp = await adminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{empId}/leave-policy",
            new { companyId, employeeId = empId, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        policyAssignResp.EnsureSuccessStatusCode();

        await EnsureBalanceAsync(companyId, empId, leaveTypeId, policy.Id);

        var submitResp = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{empId}/leave-requests",
            new
            {
                companyId,
                employeeId = empId,
                leaveTypeId,
                startDate = "2026-09-01",
                startPart = "FullDay",
                endDate   = "2026-09-05",
                endPart   = "FullDay",
                reason    = "Lifecycle rejection test"
            });
        submitResp.EnsureSuccessStatusCode();
        var leaveRequest = await submitResp.Content.ReadFromJsonAsync<LeaveRequestPayload>();

        var managerTasks = await GetEmployeeTasksAsync(adminClient, companyId, managerEmpId);
        var approvalTask = Assert.Single(
            managerTasks,
            t => t.Source == "Leave" && t.ActionType == "Approve" && t.SourceEntityId == leaveRequest!.Id);

        var completeResp = await managerClient.PostAsync(
            $"/api/companies/{companyId}/tasks/{approvalTask.Id}/complete",
            Json(new { outcomeDecision = "Reject", outcomeReason = "Team at capacity." }));
        completeResp.EnsureSuccessStatusCode();

        var listResp = await adminClient.GetAsync(
            $"/api/companies/{companyId}/employees/{empId}/leave-requests");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<LeaveListPayload>();
        var request = Assert.Single(list!.Items);
        Assert.Equal("Rejected", request.Status);

        var balanceResp = await adminClient.GetAsync(
            $"/api/companies/{companyId}/employees/{empId}/leave-balances?policyYear={DateTimeOffset.UtcNow.Year}");
        balanceResp.EnsureSuccessStatusCode();
        var balancePayload = await balanceResp.Content.ReadFromJsonAsync<BalanceResponse>();
        var balance = balancePayload!.Balances.Single(b => b.LeaveTypeId == leaveTypeId);
        Assert.Equal(0m, balance.UsedDays);
        Assert.Equal(25m, balance.RemainingDays);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static async Task<ReferenceData> CreateReferenceDataAsync(HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept {Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType {Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc {Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"RefLeavePolicy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var posResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Title {Guid.NewGuid():N}", defaultLeavePolicyId });
        posResp.EnsureSuccessStatusCode();
        var positionProfileId = (await posResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var empTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType {Guid.NewGuid():N}" });
        empTypeResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await empTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return new ReferenceData(departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private static async Task<Guid> CreateEmployeeAsync(
        HttpClient client, Guid companyId, string firstName, string lastName, ReferenceData refData)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName,
                lastName,
                workEmail   = $"{firstName.ToLower()}.{lastName.ToLower()}.{Guid.NewGuid():N}@test.com",
                startDate   = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender      = "Male",
                employeeNumber    = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId  = refData.EmploymentTypeId,
                departmentId      = refData.DepartmentId,
                locationId        = refData.LocationId,
                positionProfileId = refData.PositionProfileId
            });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private static async Task<List<TaskItem>> GetEmployeeTasksAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var resp = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/tasks");
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<TaskListPayload>();
        return payload!.Items.ToList();
    }

    private async Task EnsureBalanceAsync(Guid companyId, Guid employeeId, Guid leaveTypeId, Guid policyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var policyYear = DateTimeOffset.UtcNow.Year;
        var exists = await db.LeaveBalances.AnyAsync(b =>
            b.CompanyId == companyId &&
            b.EmployeeId == employeeId &&
            b.LeaveTypeId == leaveTypeId &&
            b.PolicyYear == policyYear);
        if (!exists)
        {
            db.LeaveBalances.Add(LeaveBalance.Create(
                Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
                policyYear, 25m, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
    }

    private static StringContent Json(object payload) =>
        new(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private sealed record IdPayload(Guid Id);
    private sealed record ReferenceData(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId);
    private sealed record LeaveRequestPayload(Guid Id, string Status, decimal TotalDays);
    private sealed record TaskListPayload(IReadOnlyList<TaskItem> Items);
    private sealed record TaskItem(Guid Id, string Source, string ActionType, string Status, Guid? SourceEntityId);
    private sealed record LeaveListPayload(List<LeaveListItem> Items);
    private sealed record LeaveListItem(Guid Id, string Status, string? RejectionReason);
    private sealed record BalanceResponse(Guid EmployeeId, int PolicyYear, List<BalanceItem> Balances);
    private sealed record BalanceItem(Guid LeaveTypeId, decimal UsedDays, decimal RemainingDays);
}
