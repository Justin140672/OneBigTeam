using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Proves the leave:request / leave:approve / leave:manage FastEndpoints policy
/// declarations actually enforce access end-to-end over real HTTP. Company Administrator
/// is scoped to company profile/settings management only and no longer holds any of these
/// permissions — see the narrowing in HR.Modules.Identity.IdentityModule.AddRolePolicies.
/// </summary>
public class LeaveAuthorizationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid HrAdminUserId = new("dd000001-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdministratorUserId = new("dd000001-0000-0000-0000-000000000002");

    public LeaveAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        // HrAdministrator performs all setup (company/employee/leave-policy creation —
        // leave:manage / employee:manage). CompanyAdministrator is the persona under test
        // and must be forbidden from leave:request / leave:approve / leave:manage endpoints.
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdministratorUserId, SystemRoles.CompanyAdministrator);
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientForCompany(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<(HttpClient HrAdminClient, HttpClient CompanyAdminClient, Guid CompanyId, Guid LeaveTypeId)> SetupCompanyAsync()
    {
        var bootstrapClient = _factory.CreateClient();
        bootstrapClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUserId.ToString());
        bootstrapClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, HrAdminUserId.ToString());

        var createResp = await bootstrapClient.PostAsJsonAsync("/api/companies", new
        {
            name = $"Leave Auth Test {Guid.NewGuid():N}",
            addresses = new[] { new { type = "RegisteredOffice", line1 = "1 Test St", city = "London", countryCode = "GB" } }
        });
        createResp.EnsureSuccessStatusCode();
        var company = await createResp.Content.ReadFromJsonAsync<CompanyPayload>();
        var companyId = company!.Id;

        var hrAdminClient = ClientForCompany(companyId, HrAdminUserId);
        var companyAdminClient = ClientForCompany(companyId, CompanyAdministratorUserId);

        // Seed a leave type directly — no API endpoint exists for this.
        var leaveTypeId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        db.LeaveTypes.Add(LeaveType.Create(
            leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        return (hrAdminClient, companyAdminClient, companyId, leaveTypeId);
    }

    private async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)> CreateReferenceDataAsync(
        HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept {Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType {Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc {Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"RefLeavePolicy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var posResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Title {Guid.NewGuid():N}", defaultLeavePolicyId });
        posResp.EnsureSuccessStatusCode();
        var positionProfileId = (await posResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var empTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType {Guid.NewGuid():N}" });
        empTypeResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await empTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private async Task<(Guid EmployeeId, Guid PolicyId)> CreateEmployeeWithPolicyAsync(HttpClient client, Guid companyId)
    {
        var policyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = true });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<PolicyPayload>();

        var refData = await CreateReferenceDataAsync(client, companyId);

        var empResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName = "Test",
                lastName = "User",
                workEmail = $"leave.auth.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId = refData.EmploymentTypeId,
                departmentId = refData.DepartmentId,
                locationId = refData.LocationId,
                positionProfileId = refData.PositionProfileId
            });
        empResp.EnsureSuccessStatusCode();
        var employee = await empResp.Content.ReadFromJsonAsync<EmployeePayload>();

        var assignResp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee!.Id}/leave-policy",
            new { companyId, employeeId = employee.Id, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        assignResp.EnsureSuccessStatusCode();

        return (employee.Id, policy.Id);
    }

    // ── leave:request ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Submitting_Leave_Request()
    {
        var (hrAdminClient, companyAdminClient, companyId, leaveTypeId) = await SetupCompanyAsync();
        var (employeeId, _) = await CreateEmployeeWithPolicyAsync(hrAdminClient, companyId);

        var response = await companyAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests",
            new
            {
                companyId,
                employeeId,
                leaveTypeId,
                startDate = "2026-09-07",
                startPart = "FullDay",
                endDate = "2026-09-11",
                endPart = "FullDay"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── leave:approve ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Approving_Leave_Request()
    {
        var (hrAdminClient, companyAdminClient, companyId, leaveTypeId) = await SetupCompanyAsync();
        var (employeeId, _) = await CreateEmployeeWithPolicyAsync(hrAdminClient, companyId);

        var submitResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests",
            new
            {
                companyId,
                employeeId,
                leaveTypeId,
                startDate = "2026-09-14",
                startPart = "FullDay",
                endDate = "2026-09-14",
                endPart = "FullDay"
            });
        submitResp.EnsureSuccessStatusCode();
        var leaveRequest = await submitResp.Content.ReadFromJsonAsync<LeaveRequestPayload>();

        var response = await companyAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequest!.Id}/approve",
            new
            {
                companyId,
                employeeId,
                leaveRequestId = leaveRequest.Id,
                reviewedByEmployeeId = employeeId
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── leave:manage ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Creating_Leave_Policy()
    {
        var (_, companyAdminClient, companyId, _) = await SetupCompanyAsync();

        var response = await companyAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record CompanyPayload(Guid Id);
    private sealed record PolicyPayload(Guid Id);
    private sealed record EmployeePayload(Guid Id);
    private sealed record LeaveRequestPayload(Guid Id, decimal TotalDays);
    private sealed record IdPayload(Guid Id);
}
