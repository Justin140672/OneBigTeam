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
/// P2 (Ticket 4 follow-up): real-Postgres coverage for the new
/// <c>catch (DbUpdateConcurrencyException)</c> guards in <see cref="HR.Modules.Leave.Features.
/// SubmitLeaveRequest.SubmitLeaveRequestHandler"/> and <see cref="HR.Modules.Leave.Features.
/// SubmitLeaveRequestDraft.SubmitLeaveRequestDraftHandler"/>, exercised through the real
/// FastEndpoints HTTP endpoints against a leave policy with <c>RequiresApproval = false</c>
/// (auto-approval) - mirrors LeaveConcurrencyEndpointTests.cs's pattern for the pre-existing
/// Approve/Reject/Cancel endpoints. Two concurrent auto-approving submissions for the SAME
/// employee/leave-type/policy-year balance is the realistic shape of this race: two employees'
/// balances are always distinct rows, so there is no way for two *different* employees'
/// submissions to contend for one balance - the shared row can only be hit by either the same
/// employee submitting twice concurrently (covered here), or an auto-approving submission racing
/// a manual approval/rejection/cancellation of a different pending request for the same employee
/// (already covered transitively by LeaveConcurrencyEndpointTests plus this file together).
/// </summary>
[Collection("Integration")]
public class AutoApprovingLeaveSubmissionConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUser = new("1eac0005-0000-0000-0000-000000000001");

    public AutoApprovingLeaveSubmissionConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, HrAdminUser, companyId);
        return client;
    }

    private async Task<(HttpClient Client, Guid CompanyId, Guid LeaveTypeId, Guid EmployeeId)> SetupAutoApprovingEmployeeAsync(
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
            new { companyId, name = $"AutoApprove-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false, requiresApproval = false });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<IdPayload>();

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var assignResp = await client.PutAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/leave-policy",
            new { companyId, employeeId, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        assignResp.EnsureSuccessStatusCode();

        return (client, companyId, leaveTypeId, employeeId);
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
            firstName = "AutoApprove",
            lastName = "Tester",
            workEmail = $"auto.approve.tester.{Guid.NewGuid():N}@example.com",
            startDate = "2026-01-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"AA-{Guid.NewGuid():N}",
            employmentTypeId,
            departmentId,
            locationId,
            positionProfileId
        });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private static Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, Guid companyId, Guid employeeId, Guid leaveTypeId, string startDate, string endDate) =>
        client.PostAsJsonAsync(
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
                reason = "Auto-approval concurrency test"
            });

    private static async Task<Guid> CreateDraftAsync(
        HttpClient client, Guid companyId, Guid employeeId, Guid leaveTypeId, string startDate, string endDate)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/drafts",
            new
            {
                companyId,
                employeeId,
                leaveTypeId,
                startDate,
                startPart = "FullDay",
                endDate,
                endPart = "FullDay",
                reason = "Draft auto-approval concurrency test"
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestPayload>();
        return payload!.Id;
    }

    private static Task<HttpResponseMessage> SubmitDraftAsync(HttpClient client, Guid companyId, Guid employeeId, Guid draftId) =>
        client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{draftId}/submit",
            new { companyId, employeeId, leaveRequestId = draftId });

    private static async Task<BalanceItem> GetBalanceAsync(HttpClient client, Guid companyId, Guid employeeId, Guid leaveTypeId)
    {
        var year = DateTimeOffset.UtcNow.Year;
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leave-balances?policyYear={year}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<BalanceResponse>();
        return payload!.Balances.Single(b => b.LeaveTypeId == leaveTypeId);
    }

    [Fact]
    public async Task Two_Concurrent_AutoApproving_Submissions_For_The_Same_Employee_Exactly_One_Wins()
    {
        var (client, companyId, leaveTypeId, employeeId) = await SetupAutoApprovingEmployeeAsync();

        var taskA = SubmitAsync(client, companyId, employeeId, leaveTypeId, "2026-08-03", "2026-08-05"); // 3 days
        var taskB = SubmitAsync(client, companyId, employeeId, leaveTypeId, "2026-09-07", "2026-09-08"); // 2 days
        var responses = await Task.WhenAll(taskA, taskB);

        var succeeded = responses.Where(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK).ToList();
        // SubmitLeaveRequest/Endpoint.cs routes both "conflict" AND "concurrency" error codes to
        // HTTP 409, matching ApproveLeaveRequest/Endpoint.cs, so the losing request must be a clean,
        // client-retryable 409 - never a 500.
        var rejected = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        Assert.Single(succeeded);
        Assert.Single(rejected);

        var rejectedBody = await rejected[0].Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.NotNull(rejectedBody);

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        Assert.True(balance.UsedDays == 3m || balance.UsedDays == 2m);
        Assert.Equal(25m - balance.UsedDays, balance.RemainingDays);

        // Both requests were auto-approved on success or never persisted on failure - either way,
        // exactly one Approved leave request exists for this employee, never two, never zero.
        var listResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leave-requests");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<ListResponse>();
        Assert.Single(list!.Items.Where(i => i.Status == "Approved"));
    }

    [Fact]
    public async Task Losing_Draft_Submission_Leaves_The_Persisted_Draft_Unchanged_And_Still_Retryable()
    {
        var (client, companyId, leaveTypeId, employeeId) = await SetupAutoApprovingEmployeeAsync();

        // A regular (non-draft) auto-approving submission and a draft submission racing for the
        // SAME balance row - the draft's own auto-approval path mutates the identical LeaveBalance
        // row a direct submission would.
        var draftId = await CreateDraftAsync(client, companyId, employeeId, leaveTypeId, "2026-09-07", "2026-09-08"); // 2 days

        var directTask = SubmitAsync(client, companyId, employeeId, leaveTypeId, "2026-08-03", "2026-08-05"); // 3 days
        var draftSubmitTask = SubmitDraftAsync(client, companyId, employeeId, draftId);
        var responses = await Task.WhenAll(directTask, draftSubmitTask);

        var directResponse = responses[0];
        var draftResponse = responses[1];

        // SubmitLeaveRequestDraft/Endpoint.cs routes "concurrency" to 409 the same way
        // SubmitLeaveRequest/Endpoint.cs does, so a losing save surfaces as a clean 409 here too.
        //
        // Two genuinely valid outcomes exist depending on real timing, neither of which is a bug:
        //   (a) the two requests' SaveChangesAsync calls genuinely overlap on the same balance row -
        //       one gets DbUpdateConcurrencyException / 409, the other succeeds.
        //   (b) the requests happen to execute back-to-back with no real overlap (HTTP/DB scheduling
        //       is not deterministic) - both succeed against the balance's state at the time each one
        //       read it, and neither loses. This is NOT a lost update: both deductions are applied,
        //       in some order, and the final balance reflects both.
        // Only a THIRD outcome - both succeeding while only one deduction lands, or neither landing,
        // or an unhandled 500 - would indicate the fix is broken. That is what's actually asserted
        // below, rather than forcing a specific one-winner-one-loser shape that real timing does not
        // guarantee.
        if (draftResponse.StatusCode == HttpStatusCode.Conflict)
        {
            // The draft lost the race - its persisted row must be completely unchanged (still
            // Draft) and retryable.
            var getResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leave-requests");
            getResponse.EnsureSuccessStatusCode();
            var list = await getResponse.Content.ReadFromJsonAsync<ListResponse>();
            var persistedDraft = list!.Items.Single(i => i.Id == draftId);
            Assert.Equal("Draft", persistedDraft.Status);

            // Retry now succeeds against the balance's current (post-winner) state.
            var retryResponse = await SubmitDraftAsync(client, companyId, employeeId, draftId);
            Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
            var retryPayload = await retryResponse.Content.ReadFromJsonAsync<LeaveRequestPayload>();
            Assert.Equal("Approved", retryPayload!.Status);

            var balanceAfterLoss = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
            Assert.Equal(5m, balanceAfterLoss.UsedDays); // 3 (direct winner) + 2 (retried draft)
        }
        else if (directResponse.StatusCode == HttpStatusCode.Conflict)
        {
            // The draft won instead - equally valid given genuine timing non-determinism - in
            // which case the direct submission was the one turned away.
            Assert.Equal(HttpStatusCode.OK, draftResponse.StatusCode);

            var balanceAfterLoss = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
            Assert.Equal(2m, balanceAfterLoss.UsedDays); // only the draft's deduction landed
        }
        else
        {
            // No real overlap occurred - both legitimately succeeded, each against the balance
            // state it actually read. Both deductions must be reflected exactly once each.
            Assert.Equal(HttpStatusCode.Created, directResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, draftResponse.StatusCode);

            var balanceBothSucceeded = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
            Assert.Equal(5m, balanceBothSucceeded.UsedDays); // 3 + 2, never lost, never double-counted

            var finalListResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leave-requests");
            finalListResponse.EnsureSuccessStatusCode();
            var finalList = await finalListResponse.Content.ReadFromJsonAsync<ListResponse>();
            Assert.Equal(2, finalList!.Items.Count(i => i.Status == "Approved"));
        }
    }

    private sealed record IdPayload(Guid Id);
    private sealed record LeaveRequestPayload(Guid Id, string Status, decimal TotalDays);
    private sealed record BalanceResponse(Guid EmployeeId, int PolicyYear, List<BalanceItem> Balances);
    private sealed record BalanceItem(Guid LeaveTypeId, decimal EntitlementDays, decimal UsedDays, decimal AdjustmentDays, decimal RemainingDays, decimal PendingDays);
    private sealed record ListResponse(List<ListItem> Items);
    private sealed record ListItem(Guid Id, string Status);
    private sealed record ErrorPayload(string? Error, string? Code);
}
