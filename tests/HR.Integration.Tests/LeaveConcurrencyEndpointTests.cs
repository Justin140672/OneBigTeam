using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// P1 #4 (optimistic concurrency): genuine-concurrency coverage for LeaveBalance/LeaveRequest.Version,
/// run against the real Postgres-backed <see cref="ApiWebApplicationFactory"/> (not EF InMemory - see
/// HR.Modules.Leave.Tests/LeaveConcurrencyHandlerTests.cs for InMemory-level coverage of the same
/// rules with more granular assertions). Two HttpClient requests are fired via Task.WhenAll, mirroring
/// StripeWebhookConcurrencyTests, so the handlers' two invocations genuinely race each other's
/// SaveChangesAsync calls against the same row(s) under Postgres MVCC.
/// </summary>
[Collection("Integration")]
public class LeaveConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUser = new("1eac0004-0000-0000-0000-000000000001");

    public LeaveConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Two_Concurrent_Approvals_Draining_The_Same_Balance_One_Succeeds_And_Balance_Reflects_Only_The_Winner()
    {
        var (client, companyId, leaveTypeId, employeeId) = await SetupEmployeeWithBalanceAsync();

        var requestAId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId, "2026-08-03", "2026-08-05"); // 3 days
        var requestBId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId, "2026-09-07", "2026-09-08"); // 2 days

        var taskA = ApproveAsync(client, companyId, employeeId, requestAId);
        var taskB = ApproveAsync(client, companyId, employeeId, requestBId);
        var responses = await Task.WhenAll(taskA, taskB);

        var succeeded = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var conflicted = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        // Exactly one wins and one is rejected as stale - never both silently succeeding (which
        // would prove a lost update was NOT prevented) and never both failing.
        Assert.Single(succeeded);
        Assert.Single(conflicted);

        var conflictBody = await conflicted[0].Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", conflictBody!.Code);

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        // Only one of the two deductions (3 or 2 days) landed - never both (5, a phantom double
        // apply) and never neither (0, a lost update).
        Assert.True(balance.UsedDays == 3m || balance.UsedDays == 2m);
        Assert.Equal(25m - balance.UsedDays, balance.RemainingDays);
    }

    [Fact]
    public async Task Concurrent_Approve_And_Reject_Of_The_Same_Request_Produce_Exactly_One_Winner()
    {
        var (client, companyId, leaveTypeId, employeeId) = await SetupEmployeeWithBalanceAsync();

        var requestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId, "2026-08-10", "2026-08-12"); // 3 days

        var approveTask = ApproveAsync(client, companyId, employeeId, requestId);
        var rejectTask = RejectAsync(client, companyId, employeeId, requestId);
        var responses = await Task.WhenAll(approveTask, rejectTask);

        var succeeded = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var rejected = responses.Where(r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest).ToList();

        // Exactly one of the two competing transitions wins; the other is turned away either as a
        // 409 concurrency conflict (both loaded Pending, loser's save is stale) or - if the two
        // requests happen to be serialized by the test host rather than truly overlapping - as a
        // 400 business-rule failure (status no longer Pending). Either way, never both 200s.
        Assert.Single(succeeded);
        Assert.Single(rejected);

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        var status = await GetLeaveRequestStatusAsync(client, companyId, employeeId, requestId);

        // The final persisted status is exactly one of Approved/Rejected - never a corrupted or
        // ambiguous state - and the balance is consistent with whichever one actually won.
        Assert.True(status is "Approved" or "Rejected");
        if (status == "Approved")
        {
            Assert.Equal(3m, balance.UsedDays);
        }
        else
        {
            Assert.Equal(0m, balance.UsedDays);
        }
    }

    [Fact]
    public async Task Concurrent_Toil_Leave_Approvals_Against_The_Same_Toil_Balance_Do_Not_Double_Spend()
    {
        var (client, companyId, employeeId) = await SetupEmployeeWithToilBalanceAsync(awardedDays: 4m);
        var toilLeaveTypeId = await GetToilLeaveTypeIdAsync(companyId);

        // Two separate TOIL leave requests, each requesting 3 days from a 4-day pot - only one can
        // legitimately be approved without going negative (AllowNegativeToilBalance defaults false).
        var requestAId = await SubmitLeaveRequestAsync(client, companyId, employeeId, toilLeaveTypeId, "2026-08-03", "2026-08-05"); // 3 days
        var requestBId = await SubmitLeaveRequestAsync(client, companyId, employeeId, toilLeaveTypeId, "2026-09-07", "2026-09-09"); // 3 days

        var taskA = ApproveAsync(client, companyId, employeeId, requestAId);
        var taskB = ApproveAsync(client, companyId, employeeId, requestBId);
        var responses = await Task.WhenAll(taskA, taskB);

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var notSucceeded = responses.Count(r => r.StatusCode != HttpStatusCode.OK);

        // At most one of the two 3-day requests can be approved against a 4-day pot without the
        // AllowNegativeToilBalance override - proving TOIL was not double-spent under a genuine race.
        Assert.Equal(1, succeeded);
        Assert.Equal(1, notSucceeded);

        var balance = await GetBalanceAsync(client, companyId, employeeId, toilLeaveTypeId);
        Assert.Equal(3m, balance.UsedDays); // only the winner's consumption applied
        Assert.Equal(1m, balance.RemainingDays); // 4 awarded - 3 used
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, HrAdminUser, companyId);
        return client;
    }

    private async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)> CreateReferenceDataAsync(
        HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"RefLeavePolicy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var ppResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}", defaultLeavePolicyId });
        ppResp.EnsureSuccessStatusCode();
        var positionProfileId = (await ppResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var etResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/employment-types", new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var (departmentId, locationId, positionProfileId, employmentTypeId) = await CreateReferenceDataAsync(client, companyId);

        var resp = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Concurrency",
            lastName = "Tester",
            workEmail = $"conc.tester.{Guid.NewGuid():N}@example.com",
            startDate = "2026-01-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"CONC-{Guid.NewGuid():N}",
            employmentTypeId,
            departmentId,
            locationId,
            positionProfileId
        });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private async Task<(HttpClient Client, Guid CompanyId, Guid LeaveTypeId, Guid EmployeeId)> SetupEmployeeWithBalanceAsync(
        int defaultEntitlementDays = 25)
    {
        var companyId = Guid.NewGuid();
        var client = await AuthenticatedClient(companyId);

        var leaveTypeId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            db.LeaveTypes.Add(LeaveType.Create(
                leaveTypeId, companyId, "Annual Leave", "ANNUAL", defaultEntitlementDays,
                AccrualMethod.None, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var policyResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<IdPayload>();

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var assignResp = await client.PutAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/leave-policy",
            new { companyId, employeeId, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        assignResp.EnsureSuccessStatusCode();

        return (client, companyId, leaveTypeId, employeeId);
    }

    private async Task<(HttpClient Client, Guid CompanyId, Guid EmployeeId)> SetupEmployeeWithToilBalanceAsync(decimal awardedDays)
    {
        var companyId = Guid.NewGuid();
        var client = await AuthenticatedClient(companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            db.LeaveTypes.Add(LeaveType.Create(
                Guid.NewGuid(), companyId, "Time Off In Lieu", "TOIL", 0,
                AccrualMethod.None, LeaveTypeBehaviour.Toil, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var policyResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<IdPayload>();

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var assignResp = await client.PutAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/leave-policy",
            new { companyId, employeeId, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        assignResp.EnsureSuccessStatusCode();

        var awardResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/toil",
            new { companyId, employeeId, awardedByEmployeeId = HrAdminUser, days = awardedDays, occurredOn = "2026-01-15", notes = "Seed award" });
        awardResp.EnsureSuccessStatusCode();

        return (client, companyId, employeeId);
    }

    private async Task<Guid> GetToilLeaveTypeIdAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var toilType = await db.LeaveTypes.SingleAsync(lt => lt.CompanyId == companyId && lt.Behaviour == LeaveTypeBehaviour.Toil);
        return toilType.Id;
    }

    private static async Task<Guid> SubmitLeaveRequestAsync(
        HttpClient client, Guid companyId, Guid employeeId, Guid leaveTypeId, string startDate, string endDate)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests",
            new
            {
                companyId,
                employeeId,
                leaveTypeId,
                startDate,
                startPart = "FullDay",
                endDate,
                endPart = "FullDay",
                reason = "Concurrency test"
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestPayload>();
        return payload!.Id;
    }

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient client, Guid companyId, Guid employeeId, Guid leaveRequestId) =>
        client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/approve",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = HrAdminUser });

    private static Task<HttpResponseMessage> RejectAsync(HttpClient client, Guid companyId, Guid employeeId, Guid leaveRequestId) =>
        client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/reject",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = HrAdminUser, rejectionReason = "Racing reject" });

    private static async Task<BalanceItem> GetBalanceAsync(HttpClient client, Guid companyId, Guid employeeId, Guid leaveTypeId)
    {
        var year = DateTimeOffset.UtcNow.Year;
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leave-balances?policyYear={year}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<BalanceResponse>();
        return payload!.Balances.Single(b => b.LeaveTypeId == leaveTypeId);
    }

    private static async Task<string> GetLeaveRequestStatusAsync(HttpClient client, Guid companyId, Guid employeeId, Guid leaveRequestId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leave-requests");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ListResponse>();
        return payload!.Items.Single(i => i.Id == leaveRequestId).Status;
    }

    private sealed record IdPayload(Guid Id);
    private sealed record LeaveRequestPayload(Guid Id, string Status, decimal TotalDays);
    private sealed record BalanceResponse(Guid EmployeeId, int PolicyYear, List<BalanceItem> Balances);
    private sealed record BalanceItem(Guid LeaveTypeId, decimal EntitlementDays, decimal UsedDays, decimal AdjustmentDays, decimal RemainingDays, decimal PendingDays);
    private sealed record ListResponse(List<ListItem> Items);
    private sealed record ListItem(Guid Id, string Status);
    private sealed record ErrorPayload(string? Error, string? Code);
}
