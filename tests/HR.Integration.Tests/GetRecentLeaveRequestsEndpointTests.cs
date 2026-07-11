using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
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
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
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

    private async Task<Guid> SeedEmployeeAsync(Guid companyId, string firstName, string lastName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var employee = Employee.Create(Guid.NewGuid(), companyId, firstName, lastName, $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(), new DateOnly(2026, 1, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
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

    private async Task<Guid> SeedLeaveRequestAsync(Guid companyId, Guid employeeId, Guid leaveTypeId, DateTimeOffset createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var request = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            new DateOnly(2026, 7, 1), LeaveDayPart.FullDay, new DateOnly(2026, 7, 3), LeaveDayPart.FullDay,
            3m, "Trip", createdAt);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
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
        DateTimeOffset CreatedAt);
}
