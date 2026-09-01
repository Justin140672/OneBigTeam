using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// LEAVE-01: GET .../employees/{employeeId}/leave-requests/{id}. Resource-authorization
/// (self / manager-in-hierarchy / HR-admin) is covered by <see cref="LeaveResourceAuthorizationTests"/>;
/// this class pins the handler's projection, tenant/employee scoping and 404 behaviour.
/// </summary>
[Collection("Integration")]
public class GetLeaveRequestEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid SeededCompanyId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnnualLeaveTypeId  = Guid.Parse("A0000000-0000-0000-0000-000000000001");
    private static readonly Guid EmploymentTypeId   = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DepartmentId       = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId         = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId  = Guid.Parse("20000000-0000-0000-0000-000000000002");

    public GetLeaveRequestEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string Url(Guid companyId, Guid employeeId, Guid id) =>
        $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{id}";

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url(SeededCompanyId, Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var owner = await CreateEmployeeAsync();
        var peer  = await CreateEmployeeAsync();
        var id = await SeedLeaveRequestAsync(owner);

        using var client = await AuthenticatedClient(peer);

        var response = await client.GetAsync(Url(SeededCompanyId, owner, id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Can_Read_Own_Request_With_Resolved_LeaveType_Name()
    {
        var employee = await CreateEmployeeAsync();
        var id = await SeedLeaveRequestAsync(employee);

        using var client = await AuthenticatedClient(employee);

        var response = await client.GetAsync(Url(SeededCompanyId, employee, id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestPayload>();
        Assert.NotNull(payload);
        Assert.Equal(id, payload!.Id);
        Assert.Equal(AnnualLeaveTypeId, payload.LeaveTypeId);
        Assert.False(string.IsNullOrWhiteSpace(payload.LeaveTypeName));
        Assert.Equal("Pending", payload.Status);
        Assert.Equal(new DateOnly(2026, 7, 1), payload.StartDate);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Id()
    {
        var employee = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(employee);

        var response = await client.GetAsync(Url(SeededCompanyId, employee, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_When_Route_EmployeeId_Does_Not_Own_The_Request()
    {
        var owner  = await CreateEmployeeAsync();
        var caller = await CreateEmployeeAsync();
        var id = await SeedLeaveRequestAsync(owner);

        using var client = await AuthenticatedClient(caller);

        // Passes the self authorizer (route employeeId == caller) but the handler's EmployeeId
        // predicate excludes the row -> 404.
        var response = await client.GetAsync(Url(SeededCompanyId, caller, id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_When_Route_CompanyId_Does_Not_Match_The_Request()
    {
        var employee = await CreateEmployeeAsync();
        var id = await SeedLeaveRequestAsync(employee);

        var otherCompany = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employee.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, otherCompany.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, employee, SystemRoles.Employee, otherCompany);

        // Self authorizer still passes (caller id == route employeeId), but the leave request row
        // is scoped to SeededCompanyId, so the cross-company lookup misses -> 404.
        var response = await client.GetAsync(Url(otherCompany, employee, id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        client.Dispose();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid userId, bool hrAdministrator = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, SeededCompanyId);
        if (hrAdministrator)
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, SeededCompanyId);
        return client;
    }

    private async Task<Guid> CreateEmployeeAsync()
    {
        using var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);
        var unique = Guid.NewGuid().ToString("N")[..12];

        var response = await setupClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId = SeededCompanyId,
                firstName = "GetReq",
                lastName = $"Test-{unique}",
                workEmail = $"get.leave.{unique}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"GLN-{unique}",
                employmentTypeId = EmploymentTypeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private async Task<Guid> SeedLeaveRequestAsync(Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var now = DateTimeOffset.UtcNow;

        var request = LeaveRequest.Create(
            Guid.NewGuid(), SeededCompanyId, employeeId, AnnualLeaveTypeId, Guid.NewGuid(),
            new DateOnly(2026, 7, 1), LeaveDayPart.FullDay,
            new DateOnly(2026, 7, 3), LeaveDayPart.FullDay,
            3m, "Trip", now);

        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private sealed record IdPayload(Guid Id);

    private sealed record LeaveRequestPayload(
        Guid Id,
        Guid LeaveTypeId,
        string LeaveTypeName,
        string Status,
        DateOnly StartDate,
        string StartPart,
        DateOnly EndDate,
        string EndPart,
        decimal TotalDays,
        string? Reason,
        string? RejectionReason,
        DateTimeOffset CreatedAt);
}
