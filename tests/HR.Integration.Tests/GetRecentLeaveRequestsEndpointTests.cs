using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class GetRecentLeaveRequestsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("cc000007-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetRecentLeaveRequestsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        // The existing tests in this file exercise the company-wide, all-statuses view that
        // predates viewer scoping — seed UserId as HR Administrator so that behavior is preserved
        // (mirrors AuditHistoryIntegrationTests/GetTeamTasksEndpointTests constructors).
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private HttpClient ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_RecentLeaveRequests_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/leave-requests/recent");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(-1)]
    public async Task Get_RecentLeaveRequests_Returns_UnprocessableEntity_For_Take_Out_Of_Range(int take)
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-requests/recent?take={take}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_RecentLeaveRequests_Returns_Empty_List_When_No_Requests()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-requests/recent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RecentPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_RecentLeaveRequests_Defaults_To_Ten_Most_Recent_Ordered_By_CreatedAt_Descending()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await SeedEmployeeAsync(companyId, "Alice", "Smith");
        var leaveTypeId = await SeedLeaveTypeAsync(companyId);

        var createdIds = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            createdIds.Add(await SeedLeaveRequestAsync(companyId, employeeId, leaveTypeId, Now.AddDays(-i)));
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-requests/recent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RecentPayload>();
        Assert.NotNull(payload);
        Assert.Equal(10, payload!.Items.Count);
        Assert.True(payload.Items.SequenceEqual(payload.Items.OrderByDescending(i => i.CreatedAt)));
        // The 10 most recently created requests are indices 0..9 (smallest AddDays offset).
        Assert.Equal(createdIds.Take(10).ToHashSet(), payload.Items.Select(i => i.LeaveRequestId).ToHashSet());
    }

    [Fact]
    public async Task Get_RecentLeaveRequests_Respects_Explicit_Take_Value()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await SeedEmployeeAsync(companyId, "Bob", "Jones");
        var leaveTypeId = await SeedLeaveTypeAsync(companyId);

        for (var i = 0; i < 5; i++)
        {
            await SeedLeaveRequestAsync(companyId, employeeId, leaveTypeId, Now.AddDays(-i));
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-requests/recent?take=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RecentPayload>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.Items.Count);
    }

    [Fact]
    public async Task Get_RecentLeaveRequests_Resolves_Employee_And_LeaveType_Names()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await SeedEmployeeAsync(companyId, "Carol", "White");
        var leaveTypeId = await SeedLeaveTypeAsync(companyId, "Annual Leave");

        await SeedLeaveRequestAsync(companyId, employeeId, leaveTypeId, Now);

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-requests/recent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RecentPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal("Carol White", item.EmployeeName);
        Assert.Equal("Annual Leave", item.LeaveTypeName);
        Assert.Equal("Pending", item.Status);
    }

    [Fact]
    public async Task Get_RecentLeaveRequests_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var otherEmployeeId = await SeedEmployeeAsync(otherCompanyId, "Dave", "Brown");
        var otherLeaveTypeId = await SeedLeaveTypeAsync(otherCompanyId);
        await SeedLeaveRequestAsync(otherCompanyId, otherEmployeeId, otherLeaveTypeId, Now);

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-requests/recent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RecentPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    // ── Manager scoping ────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_RecentLeaveRequests_Manager_Sees_Only_Direct_Reports_Pending_Requests()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(companyId);

        var managerId = await SeedEmployeeAsync(companyId, "Meredith", "Manager");
        var reportId = await SeedEmployeeAsync(companyId, "Ricky", "Report");
        var outsiderId = await SeedEmployeeAsync(companyId, "Olly", "Outsider");
        await AssignManagerAsync(hrAdminClient, companyId, reportId, managerId);

        var leaveTypeId = await SeedLeaveTypeAsync(companyId);

        var reportPendingId = await SeedLeaveRequestAsync(companyId, reportId, leaveTypeId, Now);
        var reportApprovedId = await SeedLeaveRequestAsync(companyId, reportId, leaveTypeId, Now.AddDays(-1));
        await ApproveLeaveRequestAsync(reportApprovedId);
        await SeedLeaveRequestAsync(companyId, outsiderId, leaveTypeId, Now);
        var managerOwnPendingId = await SeedLeaveRequestAsync(companyId, managerId, leaveTypeId, Now);

        using var managerClient = ClientFor(companyId, managerId);
        var response = await managerClient.GetAsync($"/api/companies/{companyId}/leave-requests/recent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RecentPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(reportPendingId, item.LeaveRequestId);
        Assert.NotEqual(reportApprovedId, item.LeaveRequestId);
        Assert.NotEqual(managerOwnPendingId, item.LeaveRequestId);
    }

    [Fact]
    public async Task Get_RecentLeaveRequests_HrAdministrator_Sees_All_Company_Requests_Regardless_Of_Requester()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(companyId);

        var employeeAId = await SeedEmployeeAsync(companyId, "Faye", "AlphaEmp");
        var employeeBId = await SeedEmployeeAsync(companyId, "Gareth", "BetaEmp");
        var leaveTypeId = await SeedLeaveTypeAsync(companyId);

        await SeedLeaveRequestAsync(companyId, employeeAId, leaveTypeId, Now);
        // Approved but not yet started, so it's still expected to show — see
        // Get_RecentLeaveRequests_HrAdministrator_Hides_Approved_Requests_Once_Started for the
        // other side of this rule.
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var approvedId = await SeedLeaveRequestAsync(
            companyId, employeeBId, leaveTypeId, Now.AddDays(-1),
            startDate: today.AddDays(5), endDate: today.AddDays(7));
        await ApproveLeaveRequestAsync(approvedId);

        var response = await hrAdminClient.GetAsync($"/api/companies/{companyId}/leave-requests/recent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RecentPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
    }

    [Fact]
    public async Task Get_RecentLeaveRequests_HrAdministrator_Hides_Approved_Requests_Once_Started()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(companyId);

        var employeeId = await SeedEmployeeAsync(companyId, "Ivy", "Started");
        var leaveTypeId = await SeedLeaveTypeAsync(companyId);
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        var startedYesterdayId = await SeedLeaveRequestAsync(
            companyId, employeeId, leaveTypeId, Now, startDate: today.AddDays(-1), endDate: today.AddDays(1));
        await ApproveLeaveRequestAsync(startedYesterdayId);
        var startsTodayId = await SeedLeaveRequestAsync(
            companyId, employeeId, leaveTypeId, Now, startDate: today, endDate: today.AddDays(2));
        await ApproveLeaveRequestAsync(startsTodayId);

        var response = await hrAdminClient.GetAsync($"/api/companies/{companyId}/leave-requests/recent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RecentPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private async Task<Guid> SeedEmployeeAsync(Guid companyId, string firstName, string lastName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var employee = Employee.Create(Guid.NewGuid(), companyId, firstName, lastName, $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(), new DateOnly(2026, 1, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}", refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, Now);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private async Task<Guid> SeedLeaveTypeAsync(Guid companyId, string name = "Annual Leave")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, name, name.ToUpperInvariant(), 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        db.LeaveTypes.Add(leaveType);
        await db.SaveChangesAsync();
        return leaveType.Id;
    }

    private async Task<Guid> SeedLeaveRequestAsync(
        Guid companyId, Guid employeeId, Guid leaveTypeId, DateTimeOffset createdAt,
        DateOnly? startDate = null, DateOnly? endDate = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var request = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            startDate ?? new DateOnly(2026, 7, 1), LeaveDayPart.FullDay,
            endDate ?? new DateOnly(2026, 7, 3), LeaveDayPart.FullDay,
            3m, "Trip", createdAt);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task ApproveLeaveRequestAsync(Guid leaveRequestId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var request = await db.LeaveRequests.SingleAsync(r => r.Id == leaveRequestId);
        request.Approve(Guid.NewGuid(), Now);
        await db.SaveChangesAsync();
    }

    private static async Task AssignManagerAsync(HttpClient hrAdminClient, Guid companyId, Guid employeeId, Guid managerId)
    {
        var response = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/manager",
            new { companyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    private sealed record RecentPayload(List<RecentItem> Items);

    private sealed record RecentItem(
        Guid LeaveRequestId,
        Guid EmployeeId,
        string EmployeeName,
        string LeaveTypeName,
        string Status,
        DateOnly StartDate,
        DateOnly EndDate,
        decimal TotalDays,
        DateTimeOffset CreatedAt,
        Guid? TaskId);
}
