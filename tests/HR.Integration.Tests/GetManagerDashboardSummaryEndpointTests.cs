using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// DSH-06 stage 1: the Manager bounded dashboard summary endpoint
/// (GET /api/companies/{companyId}/dashboards/manager/summary). Same cross-module composer as the HR
/// variant but gated only by "reporting:view-workload-actions" (Manager OR HrAdministrator) — there is
/// no managerId route param, the acting manager is the caller and each provider self-scopes a manager
/// to their full reporting sub-tree (DSH-02). Reporting-line data is seeded via the real AssignManager
/// HTTP endpoint using a dedicated HR bootstrap client, mirroring
/// GetWorkloadActionsEndpointTests.Get_WorkloadActions_Manager_Only_Sees_Own_DirectReports_Items.
/// </summary>
[Collection("Integration")]
public class GetManagerDashboardSummaryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.Date);

    public GetManagerDashboardSummaryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/dashboards/manager/summary";

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId, params Guid[] roles)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        foreach (var role in roles)
        {
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, role, companyId);
        }

        return client;
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.Employee);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Returns_Ok_For_Manager()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.Manager);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Returns_Ok_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Response_Always_Carries_Partial_Failure_Contract_Fields()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.Manager);

        var response = await client.GetAsync(Url(companyId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("allRequiredLoaded", out _));
        Assert.True(root.TryGetProperty("hasPartialFailure", out _));
        Assert.True(root.TryGetProperty("totalActionableCount", out _));
        foreach (var category in root.GetProperty("categories").EnumerateArray())
        {
            Assert.True(category.TryGetProperty("status", out _));
            Assert.True(category.TryGetProperty("actionableCount", out _));
        }
    }

    [Fact]
    public async Task Get_ManagerDashboardSummary_Scopes_Pending_Leave_To_The_Managers_Reporting_Subtree()
    {
        var companyId = Guid.NewGuid();

        var managerId = await SeedEmployeeAsync(companyId, "Meera", "Manager");
        var subManagerId = await SeedEmployeeAsync(companyId, "Sunil", "SubManager");
        var directReportId = await SeedEmployeeAsync(companyId, "Devon", "Report");
        var skipLevelReportId = await SeedEmployeeAsync(companyId, "Dana", "SkipLevel");
        var peerManagerId = await SeedEmployeeAsync(companyId, "Priya", "Peer");
        var unrelatedReportId = await SeedEmployeeAsync(companyId, "Uma", "Unrelated");

        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, peerManagerId, SystemRoles.Manager, companyId);

        var hrBootstrapUserId = Guid.NewGuid();
        using var hrClient = await ClientFor(companyId, hrBootstrapUserId, SystemRoles.HrAdministrator);
        await AssignManagerAsync(hrClient, companyId, subManagerId, managerId);
        await AssignManagerAsync(hrClient, companyId, directReportId, managerId);
        await AssignManagerAsync(hrClient, companyId, skipLevelReportId, subManagerId);
        await AssignManagerAsync(hrClient, companyId, unrelatedReportId, peerManagerId);

        await SeedLeaveRequestAsync(companyId, directReportId, Today.AddDays(5));
        await SeedLeaveRequestAsync(companyId, skipLevelReportId, Today.AddDays(6));
        await SeedLeaveRequestAsync(companyId, unrelatedReportId, Today.AddDays(7));

        using var managerClient = await ClientFor(companyId, managerId, SystemRoles.Manager);
        var payload = await managerClient.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        Assert.NotNull(payload);
        var leave = payload!.Categories.Single(c => c.Category == "Pending Leave Approvals");
        var employeeIds = leave.Items.Select(i => i.EmployeeId).ToList();
        Assert.Contains(directReportId, employeeIds);
        Assert.Contains(skipLevelReportId, employeeIds);
        Assert.DoesNotContain(unrelatedReportId, employeeIds);
        Assert.Equal(2, leave.ActionableCount);
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

    private async Task SeedLeaveRequestAsync(Guid companyId, Guid employeeId, DateOnly startDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        db.LeaveRequests.Add(LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
            startDate, LeaveDayPart.FullDay, startDate.AddDays(3), LeaveDayPart.FullDay,
            3m, "Trip", Now));
        await db.SaveChangesAsync();
    }

    private sealed record SummaryPayload(
        List<CategoryPayload> Categories,
        int TotalActionableCount,
        bool AllRequiredLoaded,
        bool HasPartialFailure,
        DateOnly AsOfDate);

    private sealed record CategoryPayload(
        string Category,
        string Status,
        bool Required,
        int ActionableCount,
        bool IsTruncated,
        List<ActionItemPayload> Items);

    private sealed record ActionItemPayload(
        Guid? EmployeeId,
        string EmployeeName,
        string? Department,
        string ActionType,
        string Category,
        DateOnly? DueDate,
        string Urgency,
        bool IsOverdue,
        string Status,
        string DeepLinkUrl,
        Guid? TaskId);
}
