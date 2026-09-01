using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// AUD-07: GET .../companies/{companyId}/leave-requests/{leaveRequestId}/audit-history.
/// Gated by <c>employee:manage</c> (HR Administrator). The handler reads the real
/// AuditDbContext via IAuditHistoryReader, ordered newest-first, and scoped by companyId.
///
/// Red until the foreign AUD-04 "actor_type" audit migration is fixed. Write the test correctly anyway.
/// </summary>
[Collection("Integration")]
public class GetLeaveRequestAuditHistoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid SeededCompanyId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnnualLeaveTypeId  = Guid.Parse("A0000000-0000-0000-0000-000000000001");
    private static readonly Guid EmploymentTypeId   = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DepartmentId       = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId         = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId  = Guid.Parse("20000000-0000-0000-0000-000000000002");

    public GetLeaveRequestAuditHistoryEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string Url(Guid companyId, Guid leaveRequestId) =>
        $"/api/companies/{companyId}/leave-requests/{leaveRequestId}/audit-history";

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url(SeededCompanyId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Plain_Employee()
    {
        var user = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, user, SystemRoles.Employee, SeededCompanyId);

        var response = await client.GetAsync(Url(SeededCompanyId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        client.Dispose();
    }

    [Fact]
    public async Task Returns_Ordered_History_For_A_Leave_Request_With_Submit_And_Reject_Events()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee);

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);
        var rejectResp = await hrClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}/reject",
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId, rejectionReason = "cover" });
        rejectResp.EnsureSuccessStatusCode();

        var response = await hrClient.GetAsync(Url(SeededCompanyId, leaveRequestId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.Items.Count >= 2, $"expected >= 2 audit items, got {payload.Items.Count}");

        var occurred = payload.Items.Select(i => i.OccurredAt).ToList();
        Assert.True(occurred.SequenceEqual(occurred.OrderByDescending(x => x)), "items should be newest-first");
    }

    [Fact]
    public async Task Returns_Empty_History_For_Unknown_Leave_Request()
    {
        // NOTE: the handler always returns Result.Success (never a failure), so an unknown id
        // yields 200 + empty Items rather than 404. Documenting current behaviour.
        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await hrClient.GetAsync(Url(SeededCompanyId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Does_Not_Return_History_For_A_Leave_Request_In_Another_Company()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee);

        var otherCompany = Guid.NewGuid();
        var client = _factory.CreateClient();
        var hrUser = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, hrUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, otherCompany.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUser, SystemRoles.HrAdministrator, otherCompany);

        var response = await client.GetAsync(Url(otherCompany, leaveRequestId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
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
                firstName = "LeaveAudit",
                lastName = $"Test-{unique}",
                workEmail = $"leave.audit.{unique}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"LAH-{unique}",
                employmentTypeId = EmploymentTypeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task AssignPolicyAsync(Guid employeeId)
    {
        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var policyResponse = await hrClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies",
            new { companyId = SeededCompanyId, name = $"AuditHistPolicy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = true });
        policyResponse.EnsureSuccessStatusCode();
        var policyId = (await policyResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var assignResponse = await hrClient.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/leave-policy",
            new { companyId = SeededCompanyId, employeeId, leavePolicyId = policyId, effectiveFrom = "2026-01-01" });
        assignResponse.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> SubmitAsync(HttpClient client, Guid employeeId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/leave-requests",
            new
            {
                companyId = SeededCompanyId,
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-09-01",
                startPart = "FullDay",
                endDate = "2026-09-01",
                endPart = "FullDay"
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private sealed record IdPayload(Guid Id);
    private sealed record HistoryPayload(List<HistoryItem> Items);
    private sealed record HistoryItem(DateTimeOffset OccurredAt, string Action, string User);
}
