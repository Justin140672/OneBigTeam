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
/// leave:manage is granted to HrAdministrator only (see LeavePolicyCrudEndpointTests;
/// CompanyAdministrator is scoped to company profile/settings and does not hold it) —
/// Manager has leave:approve but NOT leave:manage.
/// </summary>
[Collection("Integration")]
public class GetLeaveBalanceHistoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid HrAdminUser = new("d1d20001-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUser = new("d1d20001-0000-0000-0000-000000000002");

    public GetLeaveBalanceHistoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Manager);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_BalanceHistory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/leave-types/{Guid.NewGuid()}/balance-history");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_BalanceHistory_Returns_Forbidden_For_Caller_Without_LeaveManage_Policy()
    {
        var companyId = Guid.NewGuid();
        using var managerClient = await AuthenticatedClient(ManagerUser, companyId);

        var response = await managerClient.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/leave-types/{Guid.NewGuid()}/balance-history");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_BalanceHistory_Returns_NotFound_When_LeaveType_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(HrAdminUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/leave-types/{Guid.NewGuid()}/balance-history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_BalanceHistory_Returns_Empty_List_For_Employee_With_No_History_On_A_Real_LeaveType()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(HrAdminUser, companyId);

        var leaveTypeId = await CreateLeaveTypeAsync(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/leave-types/{leaveTypeId}/balance-history");

        // Consistent with GetEmployeeLeaveBalance, which returns 200 with empty/partial data
        // rather than 404 for a combination with no history records once the leave type itself
        // is confirmed to exist.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_BalanceHistory_Returns_Merged_And_Sorted_History_For_Seeded_Employee()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AuthenticatedClient(HrAdminUser, companyId);

        var leaveTypeId = await SeedBalanceHistoryAsync(companyId, employeeId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-types/{leaveTypeId}/balance-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(employeeId, payload!.EmployeeId);
        Assert.Equal(leaveTypeId, payload.LeaveTypeId);

        Assert.Equal(3, payload.Items.Count);
        Assert.Equal(
            ["ManualAdjustment", "ToilAward", "ApprovedLeave"],
            payload.Items.Select(i => i.Category).ToArray());
        Assert.True(payload.Items.SequenceEqual(payload.Items.OrderByDescending(i => i.Date)));
        Assert.All(payload.Items, i => Assert.Equal("Annual Leave", i.LeaveTypeName));

        // Approved leave consumes balance -> negative Change; the other two categories add to it.
        var approved = payload.Items.Single(i => i.Category == "ApprovedLeave");
        Assert.True(approved.Change < 0);
        Assert.Equal("Leave Taken", approved.Reason);

        var toil = payload.Items.Single(i => i.Category == "ToilAward");
        Assert.True(toil.Change > 0);
        Assert.Equal("TOIL Award", toil.Reason);

        var manual = payload.Items.Single(i => i.Category == "ManualAdjustment");
        Assert.Equal(15m, manual.Change);
        Assert.Equal("ManualAward", manual.Reason);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private async Task<Guid> CreateLeaveTypeAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var leaveTypeId = Guid.NewGuid();
        db.LeaveTypes.Add(LeaveType.Create(
            leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return leaveTypeId;
    }

    private async Task<Guid> SeedBalanceHistoryAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var leaveTypeId = Guid.NewGuid();
        db.LeaveTypes.Add(LeaveType.Create(
            leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow));

        var policyId = Guid.NewGuid();
        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId, DateTimeOffset.UtcNow.Year, 25m,
            new DateOnly(DateTimeOffset.UtcNow.Year, 1, 1), DateTimeOffset.UtcNow);
        db.LeaveBalances.Add(balance);

        var approved = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
            new DateOnly(2026, 1, 5), LeaveDayPart.FullDay, new DateOnly(2026, 1, 9), LeaveDayPart.FullDay,
            4m, "Family trip", new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        approved.Approve(Guid.NewGuid(), new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero));
        db.LeaveRequests.Add(approved);

        var toilTransaction = ToilTransaction.Create(
            Guid.NewGuid(), companyId, employeeId, balance.Id, Guid.NewGuid(),
            1m, new DateOnly(2026, 2, 1), "Overtime", new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero));
        db.ToilTransactions.Add(toilTransaction);

        // 2 days at the default 7.5 hours/day working pattern (no employee/company override
        // exists for this ad-hoc test data) converts to 15 hours, matching this test's
        // Change == 15m assertion below.
        var manualAdjustment = LeaveBalanceAdjustment.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId,
            2m, null, LeaveBalanceAdjustmentReason.ManualAward, "Bonus days", Guid.NewGuid(),
            new DateTimeOffset(2026, 3, 3, 9, 0, 0, TimeSpan.Zero));
        db.LeaveBalanceAdjustments.Add(manualAdjustment);

        await db.SaveChangesAsync();
        return leaveTypeId;
    }

    private sealed record HistoryPayload(Guid EmployeeId, Guid LeaveTypeId, List<HistoryItem> Items);

    private sealed record HistoryItem(
        string Category,
        DateTimeOffset Date,
        string LeaveTypeName,
        decimal Change,
        string Reason,
        decimal BalanceAfter,
        string CreatedBy,
        string Description);
}
