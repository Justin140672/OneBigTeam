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
/// LEAVE-01: POST .../employees/{employeeId}/leave-requests/{leaveRequestId}/reject.
/// Resource-authorization (manager-in-hierarchy / HR-admin / reviewer-spoofing) is covered by
/// <see cref="LeaveResourceAuthorizationTests"/>; this class pins validation, the state guard,
/// 404 behaviour and tenant scoping.
/// </summary>
[Collection("Integration")]
public class RejectLeaveRequestEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid SeededCompanyId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnnualLeaveTypeId  = Guid.Parse("A0000000-0000-0000-0000-000000000001");
    private static readonly Guid EmploymentTypeId   = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DepartmentId       = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId         = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId  = Guid.Parse("20000000-0000-0000-0000-000000000002");

    public RejectLeaveRequestEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string Url(Guid employeeId, Guid leaveRequestId) =>
        $"/api/companies/{SeededCompanyId}/employees/{employeeId}/leave-requests/{leaveRequestId}/reject";

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid(), Guid.NewGuid()), new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Plain_Employee_Without_Approve_Permission()
    {
        var employee = await CreateEmployeeAsync();
        var caller   = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, LeaveRequestStatus.Pending);

        using var client = await AuthenticatedClient(caller);

        var response = await client.PostAsJsonAsync(
            Url(employee, leaveRequestId),
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Can_Reject_A_Pending_Request_Without_A_Reason()
    {
        // The FluentValidation rules make RejectionReason optional (MaximumLength only). If the
        // product intent is that a reason is mandatory, this test documents the current gap.
        var employee = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, LeaveRequestStatus.Pending);

        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.PostAsJsonAsync(
            Url(employee, leaveRequestId),
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RejectionPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Rejected", payload!.Status);
    }

    [Fact]
    public async Task HrAdministrator_Can_Reject_A_Pending_Request_With_A_Reason()
    {
        var employee = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, LeaveRequestStatus.Pending);

        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.PostAsJsonAsync(
            Url(employee, leaveRequestId),
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId, rejectionReason = "Insufficient cover" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RejectionPayload>();
        Assert.Equal("Insufficient cover", payload!.RejectionReason);
    }

    [Fact]
    public async Task RejectionReason_At_The_500_Char_Limit_Is_Accepted()
    {
        var employee = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, LeaveRequestStatus.Pending);

        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.PostAsJsonAsync(
            Url(employee, leaveRequestId),
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId, rejectionReason = new string('x', 500) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectionReason_Over_The_500_Char_Limit_Is_Rejected_By_Validation()
    {
        var employee = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, LeaveRequestStatus.Pending);

        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.PostAsJsonAsync(
            Url(employee, leaveRequestId),
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId, rejectionReason = new string('x', 501) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Leave_Request()
    {
        var employee = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.PostAsJsonAsync(
            Url(employee, Guid.NewGuid()),
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(nameof(LeaveRequestStatus.Cancelled))]
    [InlineData(nameof(LeaveRequestStatus.Rejected))]
    [InlineData(nameof(LeaveRequestStatus.Draft))]
    public async Task Returns_BadRequest_When_Request_Is_Not_Pending_Or_Approved(string statusName)
    {
        var status = Enum.Parse<LeaveRequestStatus>(statusName);
        var employee = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, status);

        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.PostAsJsonAsync(
            Url(employee, leaveRequestId),
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_Approved_Request_Can_Still_Be_Rejected()
    {
        // The negated branch of the state guard: Approved is the *other* permitted prior state.
        var employee = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, LeaveRequestStatus.Approved);

        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.PostAsJsonAsync(
            Url(employee, leaveRequestId),
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Second_Reject_Call_Is_Rejected_By_The_State_Guard()
    {
        var employee = await CreateEmployeeAsync();
        var leaveRequestId = await SeedLeaveRequestAsync(employee, LeaveRequestStatus.Pending);

        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);
        var body = new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId };

        var first  = await client.PostAsJsonAsync(Url(employee, leaveRequestId), body);
        var second = await client.PostAsJsonAsync(Url(employee, leaveRequestId), body);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
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
                firstName = "Reject",
                lastName = $"Test-{unique}",
                workEmail = $"reject.leave.{unique}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"RLN-{unique}",
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
    private sealed record RejectionPayload(Guid Id, string Status, Guid ReviewedByEmployeeId, string? RejectionReason);
}
