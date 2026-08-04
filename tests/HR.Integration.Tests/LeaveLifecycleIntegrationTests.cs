using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class LeaveLifecycleIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("dddddddd-0000-0000-0000-000000000001");

    public LeaveLifecycleIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    // ─── Happy-path lifecycle ──────────────────────────────────────────────────

    [Fact]
    public async Task Submit_Then_Approve_Deducts_Leave_Balance()
    {
        var (client, companyId, leaveTypeId, employeeId, policyId) = await SetupAsync();

        await SeedBalanceAsync(companyId, employeeId, leaveTypeId, policyId, entitlementDays: 25);

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-08-03", "2026-08-07"); // Mon–Fri = 5 days

        var approveResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/approve",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId });

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        Assert.Equal(5m, balance.UsedDays);
        Assert.Equal(20m, balance.RemainingDays);
    }

    [Fact]
    public async Task Submit_Then_Approve_Then_Cancel_Restores_Leave_Balance()
    {
        var (client, companyId, leaveTypeId, employeeId, policyId) = await SetupAsync();

        await SeedBalanceAsync(companyId, employeeId, leaveTypeId, policyId, entitlementDays: 25);

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-08-10", "2026-08-14"); // Mon–Fri = 5 days

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/approve",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId });

        var cancelResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        Assert.Equal(0m, balance.UsedDays);
        Assert.Equal(25m, balance.RemainingDays);
    }

    [Fact]
    public async Task Submit_Then_Reject_When_Pending_Does_Not_Affect_Balance()
    {
        var (client, companyId, leaveTypeId, employeeId, policyId) = await SetupAsync();

        await SeedBalanceAsync(companyId, employeeId, leaveTypeId, policyId, entitlementDays: 25);

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-08-17", "2026-08-21"); // Mon–Fri = 5 days

        var rejectResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/reject",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId, rejectionReason = "Not approved" });

        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        Assert.Equal(0m, balance.UsedDays);
        Assert.Equal(25m, balance.RemainingDays);
    }

    [Fact]
    public async Task Submit_Then_Approve_Then_Reject_Restores_Leave_Balance()
    {
        var (client, companyId, leaveTypeId, employeeId, policyId) = await SetupAsync();

        await SeedBalanceAsync(companyId, employeeId, leaveTypeId, policyId, entitlementDays: 25);

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-08-24", "2026-08-28"); // Mon–Fri = 5 days

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/approve",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId });

        var rejectResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/reject",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId, rejectionReason = "Approved in error" });

        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        Assert.Equal(0m, balance.UsedDays);
        Assert.Equal(25m, balance.RemainingDays);
    }

    // ─── Auth guards ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/leave-requests/{Guid.NewGuid()}/approve",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/leave-requests/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reject_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/leave-requests/{Guid.NewGuid()}/reject",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Approve_Returns_NotFound_For_Unknown_Leave_Request()
    {
        var (client, companyId, _, employeeId, _) = await SetupAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{Guid.NewGuid()}/approve",
            new { companyId, employeeId, leaveRequestId = Guid.NewGuid(), reviewedByEmployeeId = UserId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── Day count accuracy ────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_Returns_Correct_TotalDays_In_Response()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        // Mon–Fri = 5 working days
        var (_, totalDays, status) = await SubmitLeaveRequestWithPartsAsync(
            client, companyId, employeeId, leaveTypeId,
            "2026-09-07", "FullDay", "2026-09-11", "FullDay");

        Assert.Equal("Pending", status);
        Assert.Equal(5m, totalDays);
    }

    [Fact]
    public async Task Submit_Single_Day_Morning_Returns_Half_Day()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var (_, totalDays, _) = await SubmitLeaveRequestWithPartsAsync(
            client, companyId, employeeId, leaveTypeId,
            "2026-09-14", "Morning", "2026-09-14", "Morning"); // single Monday morning

        Assert.Equal(0.5m, totalDays);
    }

    [Fact]
    public async Task Submit_Multi_Day_Range_With_Half_Day_Boundaries_Returns_Correct_Days()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        // Mon morning (0.5) + Tue full (1) + Wed afternoon (0.5) = 2 days
        var (_, totalDays, _) = await SubmitLeaveRequestWithPartsAsync(
            client, companyId, employeeId, leaveTypeId,
            "2026-09-21", "Morning", "2026-09-23", "Afternoon");

        Assert.Equal(2m, totalDays);
    }

    [Fact]
    public async Task Submit_Weekend_Only_Range_Returns_BadRequest()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        // 2026-09-05 = Saturday, 2026-09-06 = Sunday — no working days
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests",
            new
            {
                companyId, employeeId, leaveTypeId,
                startDate = "2026-09-05", startPart = "FullDay",
                endDate = "2026-09-06", endPart = "FullDay",
                reason = "Weekend test"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ─── PendingDays balance tracking ─────────────────────────────────────────

    [Fact]
    public async Task Submit_Increases_PendingDays_On_Balance()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-09-28", "2026-10-02"); // Mon–Fri = 5 days

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        Assert.Equal(0m, balance.UsedDays);
        Assert.Equal(5m, balance.PendingDays);
        Assert.Equal(25m, balance.RemainingDays); // RemainingDays is unaffected by pending
    }

    [Fact]
    public async Task Approve_Clears_PendingDays_And_Increments_UsedDays()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-10-05", "2026-10-09"); // Mon–Fri = 5 days

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/approve",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId });

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        Assert.Equal(5m, balance.UsedDays);
        Assert.Equal(0m, balance.PendingDays);
        Assert.Equal(20m, balance.RemainingDays);
    }

    [Fact]
    public async Task Reject_When_Pending_Clears_PendingDays()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-10-12", "2026-10-16"); // Mon–Fri = 5 days

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/reject",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId, rejectionReason = "Test" });

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        Assert.Equal(0m, balance.UsedDays);
        Assert.Equal(0m, balance.PendingDays);
    }

    [Fact]
    public async Task Cancel_When_Pending_Clears_PendingDays_Without_Affecting_UsedDays()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-10-19", "2026-10-23"); // Mon–Fri = 5 days

        await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}");

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        Assert.Equal(0m, balance.UsedDays);
        Assert.Equal(0m, balance.PendingDays);
        Assert.Equal(25m, balance.RemainingDays);
    }

    // ─── State machine enforcement ─────────────────────────────────────────────

    [Fact]
    public async Task Approve_Already_Approved_Request_Returns_BadRequest()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-10-26", "2026-10-30");

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/approve",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId });

        var secondApprove = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/approve",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId });

        Assert.Equal(HttpStatusCode.BadRequest, secondApprove.StatusCode);
    }

    [Fact]
    public async Task Cancel_Already_Cancelled_Request_Returns_BadRequest()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-11-02", "2026-11-06");

        await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}");

        var secondCancel = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.BadRequest, secondCancel.StatusCode);
    }

    [Fact]
    public async Task Cancel_Rejected_Request_Returns_BadRequest()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-11-09", "2026-11-13");

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/reject",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId, rejectionReason = "Test" });

        var cancelAfterReject = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.BadRequest, cancelAfterReject.StatusCode);
    }

    [Fact]
    public async Task Reject_Already_Rejected_Request_Returns_BadRequest()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-11-16", "2026-11-20");

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/reject",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId, rejectionReason = "First" });

        var secondReject = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/reject",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId, rejectionReason = "Second" });

        Assert.Equal(HttpStatusCode.BadRequest, secondReject.StatusCode);
    }

    [Fact]
    public async Task Reject_Cancelled_Request_Returns_BadRequest()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-11-23", "2026-11-27");

        await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}");

        var rejectAfterCancel = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/reject",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId, rejectionReason = "Test" });

        Assert.Equal(HttpStatusCode.BadRequest, rejectAfterCancel.StatusCode);
    }

    // ─── Insufficient balance ──────────────────────────────────────────────────

    [Fact]
    public async Task Submit_Returns_BadRequest_When_Balance_Insufficient()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();
        // Policy assigns a 25-day balance automatically; request 26 days to exceed it.
        // 2026-09-07 (Mon) to 2026-10-12 (Mon) = 26 working days
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests",
            new
            {
                companyId, employeeId, leaveTypeId,
                startDate = "2026-09-07", startPart = "FullDay",
                endDate = "2026-10-12", endPart = "FullDay",
                reason = "Test"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ─── Multiple requests cumulate correctly ──────────────────────────────────

    [Fact]
    public async Task Two_Approved_Requests_Cumulate_Balance_Deduction()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        // First request: Mon–Wed = 3 days
        var req1 = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-11-30", "2026-12-02");
        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{req1}/approve",
            new { companyId, employeeId, leaveRequestId = req1, reviewedByEmployeeId = UserId });

        // Second request: Mon–Tue = 2 days
        var req2 = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-12-07", "2026-12-08");
        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{req2}/approve",
            new { companyId, employeeId, leaveRequestId = req2, reviewedByEmployeeId = UserId });

        var balance = await GetBalanceAsync(client, companyId, employeeId, leaveTypeId);
        Assert.Equal(5m, balance.UsedDays);      // 3 + 2
        Assert.Equal(20m, balance.RemainingDays); // 25 − 5
    }

    // ─── List endpoint ─────────────────────────────────────────────────────────

    [Fact]
    public async Task List_Returns_All_Requests_For_Employee_With_Correct_Fields()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var id1 = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-12-14", "2026-12-14"); // 1 day

        var id2 = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-12-15", "2026-12-16"); // 2 days

        var listResponse = await ListLeaveRequestsAsync(client, companyId, employeeId);

        Assert.Equal(2, listResponse.Items.Count);

        // Items returned newest-first (ordered by StartDate descending)
        Assert.Equal(id2, listResponse.Items[0].Id);
        Assert.Equal(id1, listResponse.Items[1].Id);

        Assert.All(listResponse.Items, item =>
        {
            Assert.Equal("Pending", item.Status);
            Assert.Equal(leaveTypeId, item.LeaveTypeId);
            Assert.Equal("Annual Leave", item.LeaveTypeName);
            Assert.Equal("FullDay", item.StartPart);
            Assert.Equal("FullDay", item.EndPart);
        });

        Assert.Equal(2m, listResponse.Items[0].TotalDays);
        Assert.Equal(1m, listResponse.Items[1].TotalDays);
    }

    [Fact]
    public async Task List_Returns_Empty_For_Employee_With_No_Requests()
    {
        var (client, companyId, _, employeeId, _) = await SetupAsync();

        var listResponse = await ListLeaveRequestsAsync(client, companyId, employeeId);

        Assert.Empty(listResponse.Items);
    }

    [Fact]
    public async Task List_Does_Not_Return_Requests_From_Other_Employees()
    {
        var (client, companyId, leaveTypeId, employeeIdA, _) = await SetupAsync();

        await SubmitLeaveRequestAsync(client, companyId, employeeIdA, leaveTypeId,
            "2026-12-21", "2026-12-21");

        // Create a second employee in the same company
        var refData = await CreateReferenceDataAsync(client, companyId);
        var empBResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName = "Other",
                lastName = "Employee",
                workEmail = $"other.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId = refData.EmploymentTypeId,
                departmentId = refData.DepartmentId,
                locationId = refData.LocationId,
                positionProfileId = refData.PositionProfileId
            });
        empBResp.EnsureSuccessStatusCode();
        var empB = await empBResp.Content.ReadFromJsonAsync<EmployeePayload>();

        var listResponse = await ListLeaveRequestsAsync(client, companyId, empB!.Id);

        Assert.Empty(listResponse.Items);
    }

    [Fact]
    public async Task Reject_With_Reason_Is_Returned_By_List_Endpoint()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var leaveRequestId = await SubmitLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-12-22", "2026-12-22");

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/reject",
            new { companyId, employeeId, leaveRequestId, reviewedByEmployeeId = UserId, rejectionReason = "Insufficient cover" });

        var listResponse = await ListLeaveRequestsAsync(client, companyId, employeeId);

        var item = Assert.Single(listResponse.Items);
        Assert.Equal("Rejected", item.Status);
        Assert.Equal("Insufficient cover", item.RejectionReason);
    }

    // ─── Preview endpoint ──────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_Returns_Correct_TotalDays()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var preview = await PreviewLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-09-07", "FullDay", "2026-09-11", "FullDay"); // Mon–Fri = 5 days

        Assert.Equal(5m, preview.TotalDays);
    }

    [Fact]
    public async Task Preview_Shows_WouldExceedBalance_When_Insufficient()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();
        // Policy auto-creates a 25-day balance; preview 26 days to exceed it.
        // 2026-09-07 (Mon) to 2026-10-12 (Mon) = 26 working days
        var preview = await PreviewLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-09-07", "FullDay", "2026-10-12", "FullDay");

        Assert.True(preview.WouldExceedBalance);
        Assert.Equal(25m, preview.RemainingBalance);
    }

    [Fact]
    public async Task Preview_Returns_RemainingBalance_When_Sufficient()
    {
        var (client, companyId, leaveTypeId, employeeId, _) = await SetupAsync();

        var preview = await PreviewLeaveRequestAsync(client, companyId, employeeId, leaveTypeId,
            "2026-09-14", "FullDay", "2026-09-18", "FullDay"); // 5 days from 25 available

        Assert.Equal(5m, preview.TotalDays);
        Assert.False(preview.WouldExceedBalance);
        Assert.Equal(25m, preview.RemainingBalance);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, Guid CompanyId, Guid LeaveTypeId, Guid EmployeeId, Guid PolicyId)> SetupAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, UserId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.HrAdministrator, UserId);

        var companyResp = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Lifecycle Test {Guid.NewGuid():N}",
            addresses = new[] { new { type = "RegisteredOffice", line1 = "1 Test St", city = "London", countryCode = "GB" } }
        });
        companyResp.EnsureSuccessStatusCode();
        var company = await companyResp.Content.ReadFromJsonAsync<CompanyPayload>();
        var companyId = company!.Id;

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var leaveTypeId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            db.LeaveTypes.Add(LeaveType.Create(
                leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
                AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var policyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<PolicyPayload>();

        var refData = await CreateReferenceDataAsync(client, companyId);

        var empResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName = "Leave",
                lastName = "Lifecycle",
                workEmail = $"lifecycle.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId = refData.EmploymentTypeId,
                departmentId = refData.DepartmentId,
                locationId = refData.LocationId,
                positionProfileId = refData.PositionProfileId
            });
        empResp.EnsureSuccessStatusCode();
        var employee = await empResp.Content.ReadFromJsonAsync<EmployeePayload>();

        var assignResp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee!.Id}/leave-policy",
            new { companyId, employeeId = employee.Id, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        assignResp.EnsureSuccessStatusCode();

        return (client, companyId, leaveTypeId, employee.Id, policy.Id);
    }

    private async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)> CreateReferenceDataAsync(
        HttpClient client, Guid companyId)
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

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private async Task SeedBalanceAsync(Guid companyId, Guid employeeId, Guid leaveTypeId, Guid policyId, decimal entitlementDays)
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
                policyYear, entitlementDays, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
    }

    private async Task<Guid> SubmitLeaveRequestAsync(
        HttpClient client, Guid companyId, Guid employeeId, Guid leaveTypeId,
        string startDate, string endDate)
    {
        var (id, _, _) = await SubmitLeaveRequestWithPartsAsync(
            client, companyId, employeeId, leaveTypeId,
            startDate, "FullDay", endDate, "FullDay");
        return id;
    }

    private async Task<(Guid Id, decimal TotalDays, string Status)> SubmitLeaveRequestWithPartsAsync(
        HttpClient client, Guid companyId, Guid employeeId, Guid leaveTypeId,
        string startDate, string startPart, string endDate, string endPart)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests",
            new
            {
                companyId,
                employeeId,
                leaveTypeId,
                startDate,
                startPart,
                endDate,
                endPart,
                reason = "Integration test"
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestPayload>();
        return (payload!.Id, payload.TotalDays, payload.Status);
    }

    private async Task<BalanceItem> GetBalanceAsync(HttpClient client, Guid companyId, Guid employeeId, Guid leaveTypeId)
    {
        var year = DateTimeOffset.UtcNow.Year;
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balances?policyYear={year}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<BalanceResponse>();
        return payload!.Balances.Single(b => b.LeaveTypeId == leaveTypeId);
    }

    private async Task<ListResponse> ListLeaveRequestsAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ListResponse>())!;
    }

    private async Task<PreviewResponse> PreviewLeaveRequestAsync(
        HttpClient client, Guid companyId, Guid employeeId, Guid leaveTypeId,
        string startDate, string startPart, string endDate, string endPart)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/preview",
            new { companyId, employeeId, leaveTypeId, startDate, startPart, endDate, endPart });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PreviewResponse>())!;
    }

    // ─── Response records ──────────────────────────────────────────────────────

    private sealed record CompanyPayload(Guid Id);
    private sealed record PolicyPayload(Guid Id);
    private sealed record EmployeePayload(Guid Id);
    private sealed record IdPayload(Guid Id);
    private sealed record LeaveRequestPayload(Guid Id, string Status, decimal TotalDays);
    private sealed record BalanceResponse(Guid EmployeeId, int PolicyYear, List<BalanceItem> Balances);
    private sealed record BalanceItem(Guid LeaveTypeId, decimal EntitlementDays, decimal UsedDays, decimal AdjustmentDays, decimal RemainingDays, decimal PendingDays);
    private sealed record ListResponse(List<ListItem> Items);
    private sealed record ListItem(Guid Id, Guid LeaveTypeId, string LeaveTypeName, string Status, DateOnly StartDate, string StartPart, DateOnly EndDate, string EndPart, decimal TotalDays, string? Reason, string? RejectionReason);
    private sealed record PreviewResponse(decimal TotalDays, decimal? RemainingBalance, bool WouldExceedBalance);
}
