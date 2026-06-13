using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class LeaveLifecycleIntegrationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("dddddddd-0000-0000-0000-000000000001");

    public LeaveLifecycleIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

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

    private async Task<(HttpClient Client, Guid CompanyId, Guid LeaveTypeId, Guid EmployeeId, Guid PolicyId)> SetupAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, UserId.ToString());

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

        var empResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new { companyId, firstName = "Leave", lastName = "Lifecycle", workEmail = $"lifecycle.{Guid.NewGuid():N}@example.com", startDate = "2026-01-01" });
        empResp.EnsureSuccessStatusCode();
        var employee = await empResp.Content.ReadFromJsonAsync<EmployeePayload>();

        var assignResp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee!.Id}/leave-policy",
            new { companyId, employeeId = employee.Id, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        assignResp.EnsureSuccessStatusCode();

        return (client, companyId, leaveTypeId, employee.Id, policy.Id);
    }

    private async Task SeedBalanceAsync(Guid companyId, Guid employeeId, Guid leaveTypeId, Guid policyId, decimal entitlementDays)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        db.LeaveBalances.Add(LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
            DateTimeOffset.UtcNow.Year, entitlementDays, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SubmitLeaveRequestAsync(
        HttpClient client, Guid companyId, Guid employeeId, Guid leaveTypeId,
        string startDate, string endDate)
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
                reason = "Integration test"
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestPayload>();
        return payload!.Id;
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

    private sealed record CompanyPayload(Guid Id);
    private sealed record PolicyPayload(Guid Id);
    private sealed record EmployeePayload(Guid Id);
    private sealed record LeaveRequestPayload(Guid Id, string Status, decimal TotalDays);
    private sealed record BalanceResponse(Guid EmployeeId, int PolicyYear, List<BalanceItem> Balances);
    private sealed record BalanceItem(Guid LeaveTypeId, decimal EntitlementDays, decimal UsedDays, decimal AdjustmentDays, decimal RemainingDays);
}
