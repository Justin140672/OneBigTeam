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
public class CancelLeaveRequestEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid SeededCompanyId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnnualLeaveTypeId  = Guid.Parse("A0000000-0000-0000-0000-000000000001");
    private static readonly Guid EmploymentTypeId   = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DepartmentId       = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId         = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId  = Guid.Parse("20000000-0000-0000-0000-000000000002");

    public CancelLeaveRequestEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/leave-requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var owner = await CreateEmployeeAsync();
        var peer  = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(owner, LeaveRequestStatus.Pending);

        using var client = await AuthenticatedClient(peer);

        var response = await client.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{owner}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_Can_Cancel_Own_Pending_Request_And_Status_Becomes_Cancelled()
    {
        var employee = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, LeaveRequestStatus.Pending);

        using var client = await AuthenticatedClient(employee);

        var response = await client.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CancelPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Cancelled", payload!.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var persisted = await db.LeaveRequests.AsNoTracking().SingleAsync(r => r.Id == leaveRequestId);
        Assert.Equal(LeaveRequestStatus.Cancelled, persisted.Status);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Leave_Request()
    {
        var employee = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(employee);

        var response = await client.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_When_Leave_Request_Belongs_To_Another_Employee()
    {
        var owner  = await CreateEmployeeAsync();
        var caller = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(owner, LeaveRequestStatus.Pending);

        using var client = await AuthenticatedClient(caller);

        var response = await client.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{caller}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(nameof(LeaveRequestStatus.Cancelled))]
    [InlineData(nameof(LeaveRequestStatus.Rejected))]
    public async Task Returns_BadRequest_When_Request_Is_In_A_Terminal_State(string statusName)
    {
        var status = Enum.Parse<LeaveRequestStatus>(statusName);
        var employee = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, status);

        using var client = await AuthenticatedClient(employee);

        var response = await client.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_BadRequest_When_Cancelling_A_Draft()
    {
        var employee = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, LeaveRequestStatus.Draft);

        using var client = await AuthenticatedClient(employee);

        var response = await client.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Second_Cancel_Call_Is_Rejected_By_The_State_Guard()
    {
        var employee = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, LeaveRequestStatus.Pending);

        using var client = await AuthenticatedClient(employee);
        var url = $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}";

        var first  = await client.DeleteAsync(url);
        var second = await client.DeleteAsync(url);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }


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
                firstName = "Cancel",
                lastName = $"Test-{unique}",
                workEmail = $"cancel.leave.{unique}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                gender = "Male",
                employeeNumber = $"CLN-{unique}",
                employmentTypeId = EmploymentTypeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private async Task<Guid> SeedLeaveRequestAsync(Guid employeeId, LeaveRequestStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var now = DateTimeOffset.UtcNow;

        var initialStatus = status == LeaveRequestStatus.Draft
            ? LeaveRequestStatus.Draft
            : LeaveRequestStatus.Pending;

        var request = LeaveRequest.Create(
            Guid.NewGuid(), SeededCompanyId, employeeId, AnnualLeaveTypeId, Guid.NewGuid(),
            new DateOnly(2026, 7, 1), LeaveDayPart.FullDay,
            new DateOnly(2026, 7, 3), LeaveDayPart.FullDay,
            3m, "Trip", now, initialStatus);

        switch (status)
        {
            case LeaveRequestStatus.Cancelled:
                request.Cancel(now);
                break;
            case LeaveRequestStatus.Rejected:
                request.Reject(Guid.NewGuid(), now, "no");
                break;
            case LeaveRequestStatus.Approved:
                request.Approve(Guid.NewGuid(), now);
                break;
        }

        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private sealed record IdPayload(Guid Id);
    private sealed record CancelPayload(Guid Id, string Status);
}
