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
/// leave:manage is granted to HrAdministrator/CompanyAdministrator only (see
/// LeavePolicyCrudEndpointTests) — Manager has leave:approve but NOT leave:manage, which makes it
/// the correct role to exercise the 403 boundary for this leave:manage-gated endpoint.
/// </summary>
public class AdjustLeaveBalanceEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid HrAdminUser = new("d1d10001-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUser = new("d1d10001-0000-0000-0000-000000000002");

    public AdjustLeaveBalanceEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Manager);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/leave-balance-adjustments",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_Returns_Forbidden_For_Caller_Without_LeaveManage_Policy()
    {
        var (companyId, leaveTypeId, employeeId, _) = await SetupEmployeeWithBalanceAsync();

        using var managerClient = AuthenticatedClient(ManagerUser, companyId);
        var response = await managerClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments",
            AdjustmentPayload(companyId, employeeId, leaveTypeId, 7.5m, "Correction"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_Returns_Created_And_Updates_Balance_On_Success()
    {
        var (companyId, leaveTypeId, employeeId, hrAdminClient) = await SetupEmployeeWithBalanceAsync();

        var response = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments",
            AdjustmentPayload(companyId, employeeId, leaveTypeId, 15m, "Correction", comments: "Corrected data entry error"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AdjustmentPayloadResponse>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal(leaveTypeId, payload.LeaveTypeId);
        Assert.Equal(15m, payload.AdjustmentHours);
        Assert.Equal(202.5m, payload.NewRemainingHours); // (25 entitlement + 2 adjustment) days * 7.5 hours/day
        Assert.Equal("Correction", payload.Reason);
        Assert.Equal("Corrected data entry error", payload.Comments);
        Assert.Equal(HrAdminUser, payload.AdjustedByEmployeeId);

        // Verify the balance was actually persisted by re-querying it.
        var balanceResponse = await hrAdminClient.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balances?policyYear={DateTimeOffset.UtcNow.Year}");
        balanceResponse.EnsureSuccessStatusCode();
        var balancePayload = await balanceResponse.Content.ReadFromJsonAsync<BalanceListPayload>();
        var balance = balancePayload!.Balances.Single(b => b.LeaveTypeId == leaveTypeId);
        Assert.Equal(27m, balance.RemainingDays);
        Assert.Equal(202.5m, balance.RemainingHours);
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_Returns_Created_And_Decreases_Balance_For_Deduction()
    {
        var (companyId, leaveTypeId, employeeId, hrAdminClient) = await SetupEmployeeWithBalanceAsync();

        var response = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments",
            AdjustmentPayload(companyId, employeeId, leaveTypeId, -7.5m, "ManualDeduction", comments: "Correcting an over-award"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AdjustmentPayloadResponse>();
        Assert.NotNull(payload);
        Assert.Equal(-7.5m, payload!.AdjustmentHours);
        Assert.Equal("ManualDeduction", payload.Reason);
        Assert.Equal(180m, payload.NewRemainingHours); // 25 entitlement days * 7.5 hours/day - 7.5 = 187.5 - 7.5

        // Verify the balance was actually persisted lower by re-querying it.
        var balanceResponse = await hrAdminClient.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balances?policyYear={DateTimeOffset.UtcNow.Year}");
        balanceResponse.EnsureSuccessStatusCode();
        var balancePayload = await balanceResponse.Content.ReadFromJsonAsync<BalanceListPayload>();
        var balance = balancePayload!.Balances.Single(b => b.LeaveTypeId == leaveTypeId);
        Assert.Equal(180m, balance.RemainingHours);
        Assert.Equal(24m, balance.RemainingDays); // 25 - 1 (7.5h / 7.5h-per-day)
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_Returns_NotFound_When_Employee_Belongs_To_A_Different_Company()
    {
        // Company A's HR admin must not be able to adjust a balance belonging to Company B's
        // employee, even by guessing/reusing a valid employeeId/leaveTypeId — cross-tenant lookups
        // must fail the existence checks (404), matching this codebase's established convention
        // for cross-company access (see e.g.
        // DeactivateAssetCategoryEndpointTests.Delete_AssetCategory_Returns_NotFound_When_Category_Belongs_To_Different_Company).
        var (companyBId, leaveTypeIdB, employeeIdB, _) = await SetupEmployeeWithBalanceAsync();

        // Re-authenticate a fresh HR admin scoped to Company A only (not Company B).
        var freshCompanyAId = Guid.NewGuid();
        using var hrAdminClientForA = AuthenticatedClient(HrAdminUser, freshCompanyAId);

        var response = await hrAdminClientForA.PostAsJsonAsync(
            $"/api/companies/{freshCompanyAId}/employees/{employeeIdB}/leave-balance-adjustments",
            AdjustmentPayload(freshCompanyAId, employeeIdB, leaveTypeIdB, 7.5m, "Correction"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Sanity check: Company B's own balance must be completely unaffected.
        using var hrAdminClientForB = AuthenticatedClient(HrAdminUser, companyBId);
        var balanceResponse = await hrAdminClientForB.GetAsync(
            $"/api/companies/{companyBId}/employees/{employeeIdB}/leave-balances?policyYear={DateTimeOffset.UtcNow.Year}");
        balanceResponse.EnsureSuccessStatusCode();
        var balancePayload = await balanceResponse.Content.ReadFromJsonAsync<BalanceListPayload>();
        var balance = balancePayload!.Balances.Single(b => b.LeaveTypeId == leaveTypeIdB);
        Assert.Equal(25m, balance.RemainingDays);
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_Returns_NotFound_When_LeaveType_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AuthenticatedClient(HrAdminUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments",
            AdjustmentPayload(companyId, employeeId, Guid.NewGuid(), 7.5m, "Correction"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        var (companyId, leaveTypeId, _, hrAdminClient) = await SetupEmployeeWithBalanceAsync();
        var nonExistentEmployeeId = Guid.NewGuid();

        var response = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{nonExistentEmployeeId}/leave-balance-adjustments",
            AdjustmentPayload(companyId, nonExistentEmployeeId, leaveTypeId, 7.5m, "Correction"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_Returns_NotFound_When_No_Balance_Exists_For_Employee_And_Type()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(HrAdminUser, companyId);

        var leaveTypeId = await CreateLeaveTypeAsync(companyId);
        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);
        // Deliberately do NOT assign a leave policy, so no LeaveBalance row is ever created.

        var response = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments",
            AdjustmentPayload(companyId, employeeId, leaveTypeId, 7.5m, "Correction"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_Returns_UnprocessableEntity_For_Zero_AdjustmentHours()
    {
        // A zero adjustment fails FluentValidation (AdjustLeaveBalanceValidator) before the
        // handler ever runs; FastEndpoints is configured to return 422 for validator failures
        // (see Program.cs — c.Errors.StatusCode = 422; and CreateAssetCategoryEndpointTests for
        // the equivalent convention on another leave:manage-style endpoint).
        var (companyId, leaveTypeId, employeeId, hrAdminClient) = await SetupEmployeeWithBalanceAsync();

        var response = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments",
            AdjustmentPayload(companyId, employeeId, leaveTypeId, 0m, "Correction"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_Returns_UnprocessableEntity_When_AdjustmentHours_Exceeds_Column_Precision()
    {
        // adjustment_hours/adjustment_days are numeric(6,2) columns (max magnitude 9999.99) —
        // a wildly large value like 100,000 must fail FluentValidation with a clean 422 rather
        // than reach SaveChangesAsync and blow up with a Postgres "numeric field overflow".
        var (companyId, leaveTypeId, employeeId, hrAdminClient) = await SetupEmployeeWithBalanceAsync();

        var response = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments",
            AdjustmentPayload(companyId, employeeId, leaveTypeId, 100_000m, "Correction"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_Returns_BadRequest_When_Negative_Adjustment_Would_Go_Below_Zero_Without_Override()
    {
        // 5 days entitlement, no negative override allowed on the policy.
        var (companyId, leaveTypeId, employeeId, hrAdminClient) =
            await SetupEmployeeWithBalanceAsync(defaultEntitlementDays: 5, allowNegativeBalance: false);

        var response = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments",
            AdjustmentPayload(companyId, employeeId, leaveTypeId, -60m, "ManualDeduction", allowNegativeOverride: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static object AdjustmentPayload(
        Guid companyId,
        Guid employeeId,
        Guid leaveTypeId,
        decimal adjustmentHours,
        string reason,
        string? comments = "Integration test adjustment",
        bool allowNegativeOverride = false) => new
        {
            companyId,
            employeeId,
            leaveTypeId,
            adjustmentHours,
            reason,
            comments,
            allowNegativeOverride
        };

    private async Task<Guid> CreateLeaveTypeAsync(Guid companyId, int defaultEntitlementDays = 25)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var leaveTypeId = Guid.NewGuid();
        db.LeaveTypes.Add(LeaveType.Create(
            leaveTypeId, companyId, "Annual Leave", "ANNUAL", defaultEntitlementDays,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return leaveTypeId;
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName = "Balance",
                lastName = "Tester",
                workEmail = $"balance.tester.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male"
            });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    /// <summary>
    /// Creates a fresh company, a standard leave type, a leave policy, and an employee, then
    /// assigns the policy to the employee (which auto-initialises a LeaveBalance row for every
    /// active leave type in the company — see AssignLeavePolicyToEmployeeHandler).
    /// </summary>
    private async Task<(Guid CompanyId, Guid LeaveTypeId, Guid EmployeeId, HttpClient HrAdminClient)> SetupEmployeeWithBalanceAsync(
        int defaultEntitlementDays = 25,
        bool allowNegativeBalance = false)
    {
        var companyId = Guid.NewGuid();
        var hrAdminClient = AuthenticatedClient(HrAdminUser, companyId);

        var leaveTypeId = await CreateLeaveTypeAsync(companyId, defaultEntitlementDays);

        var policyResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new
            {
                companyId,
                name = $"Policy {Guid.NewGuid():N}",
                carryOverDays = 0,
                allowNegativeBalance
            });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<IdPayload>();

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);

        var assignResp = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-policy",
            new { companyId, employeeId, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        assignResp.EnsureSuccessStatusCode();

        return (companyId, leaveTypeId, employeeId, hrAdminClient);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record AdjustmentPayloadResponse(
        Guid AdjustmentId,
        Guid CompanyId,
        Guid EmployeeId,
        Guid LeaveTypeId,
        Guid LeaveBalanceId,
        decimal AdjustmentHours,
        decimal NewRemainingHours,
        string Reason,
        string? Comments,
        Guid AdjustedByEmployeeId,
        DateTimeOffset AdjustedAt);

    private sealed record BalanceListPayload(Guid EmployeeId, int PolicyYear, List<BalanceItem> Balances);
    private sealed record BalanceItem(Guid LeaveTypeId, bool HasBalance, decimal? RemainingDays, decimal? RemainingHours);
}
