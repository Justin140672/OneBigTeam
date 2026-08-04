using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SubmitLeaveRequestEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("bbbbbbbb-0000-0000-0000-000000000002");

    // Pre-seeded leave type for the seeded company (see LeaveModule.SeedLeaveAsync)
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnnualLeaveTypeId = Guid.Parse("A0000000-0000-0000-0000-000000000001");

    public SubmitLeaveRequestEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_LeaveRequest_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = SeededCompanyId;
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests",
            new
            {
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-08-03",
                startPart = "FullDay",
                endDate = "2026-08-07",
                endPart = "FullDay"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_LeaveRequest_Returns_NotFound_When_LeaveType_Does_Not_Exist()
    {
        using var client = _factory.CreateClient();
        var companyId = SeededCompanyId;
        var employeeId = Guid.NewGuid();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests",
            new
            {
                companyId,
                employeeId,
                leaveTypeId = Guid.NewGuid(),
                startDate = "2026-08-01",
                endDate = "2026-08-05"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_LeaveRequest_Returns_Created_With_Pending_Status()
    {
        using var client = _factory.CreateClient();
        var companyId = SeededCompanyId;

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        // Create a leave policy with AllowNegativeBalance so no balance initialisation is required
        var policyResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new
            {
                companyId,
                name = $"Integration Test Policy {Guid.NewGuid():N}",
                carryOverDays = 0,
                allowNegativeBalance = true
            });
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<PolicyPayload>();

        // Create an employee
        var employeeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName = "Leave",
                lastName = "Tester",
                workEmail = $"leave.tester.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"LT-{Guid.NewGuid():N}",
                employmentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002")
            });
        employeeResponse.EnsureSuccessStatusCode();
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        // Assign the policy to the employee
        var assignResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee!.Id}/leave-policy",
            new
            {
                companyId,
                employeeId = employee.Id,
                leavePolicyId = policy!.Id,
                effectiveFrom = "2026-01-01"
            });
        assignResponse.EnsureSuccessStatusCode();

        // Submit leave request
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/leave-requests",
            new
            {
                companyId,
                employeeId = employee.Id,
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-08-03",
                startPart = "FullDay",
                endDate = "2026-08-07",
                endPart = "FullDay",
                reason = "Integration test leave"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Pending", payload!.Status);
        Assert.Equal(5m, payload.TotalDays);
        Assert.Equal(employee.Id, payload.EmployeeId);
        Assert.Equal(AnnualLeaveTypeId, payload.LeaveTypeId);
    }

    private sealed record PolicyPayload(Guid Id, Guid CompanyId, string Name, bool AllowNegativeBalance);
    private sealed record EmployeePayload(Guid Id, Guid CompanyId, string Status);
    private sealed record LeaveRequestPayload(Guid Id, Guid CompanyId, Guid EmployeeId, Guid LeaveTypeId, string Status, decimal TotalDays);
}
