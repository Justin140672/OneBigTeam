using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SubmitLeaveRequestDraftEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("cccccccc-0000-0000-0000-000000000003");

    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnnualLeaveTypeId = Guid.Parse("A0000000-0000-0000-0000-000000000001");

    public SubmitLeaveRequestDraftEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    private async Task<(HttpClient Client, Guid EmployeeId)> SetupEmployeeAsync()
    {
        var client = _factory.CreateClient();
        var companyId = SeededCompanyId;

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName = "Draft",
                lastName = "Submitter",
                workEmail = $"draft.submitter.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"DS-{Guid.NewGuid():N}",
                employmentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002")
            });
        employeeResponse.EnsureSuccessStatusCode();
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        return (client, employee!.Id);
    }

    private static async Task<Guid> AssignPolicyAsync(HttpClient client, Guid companyId, Guid employeeId, bool requiresApproval)
    {
        var policyResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new
            {
                companyId,
                name = $"Integration Test Policy {Guid.NewGuid():N}",
                carryOverDays = 0,
                allowNegativeBalance = true,
                requiresApproval
            });
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<PolicyPayload>();

        var assignResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-policy",
            new
            {
                companyId,
                employeeId,
                leavePolicyId = policy!.Id,
                effectiveFrom = "2026-01-01"
            });
        assignResponse.EnsureSuccessStatusCode();

        return policy.Id;
    }

    private static async Task<Guid> CreateDraftAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/drafts",
            new
            {
                companyId,
                employeeId,
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-08-03",
                startPart = "FullDay",
                endDate = "2026-08-07",
                endPart = "FullDay",
                reason = "Draft holiday"
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestDraftPayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task Post_Submit_Returns_Pending_When_Policy_Requires_Approval()
    {
        var (client, employeeId) = await SetupEmployeeAsync();
        var companyId = SeededCompanyId;
        await AssignPolicyAsync(client, companyId, employeeId, requiresApproval: true);
        var draftId = await CreateDraftAsync(client, companyId, employeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{draftId}/submit",
            new { companyId, employeeId, leaveRequestId = draftId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestDraftPayload>();
        Assert.Equal("Pending", payload!.Status);
    }

    [Fact]
    public async Task Post_Submit_Returns_Approved_When_Policy_Does_Not_Require_Approval()
    {
        var (client, employeeId) = await SetupEmployeeAsync();
        var companyId = SeededCompanyId;
        await AssignPolicyAsync(client, companyId, employeeId, requiresApproval: false);
        var draftId = await CreateDraftAsync(client, companyId, employeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{draftId}/submit",
            new { companyId, employeeId, leaveRequestId = draftId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestDraftPayload>();
        Assert.Equal("Approved", payload!.Status);
    }

    [Fact]
    public async Task Post_Submit_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = SeededCompanyId;
        var employeeId = Guid.NewGuid();
        var draftId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{draftId}/submit",
            new { companyId, employeeId, leaveRequestId = draftId });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Submit_Returns_BadRequest_When_Request_Is_Not_A_Draft()
    {
        var (client, employeeId) = await SetupEmployeeAsync();
        var companyId = SeededCompanyId;
        await AssignPolicyAsync(client, companyId, employeeId, requiresApproval: true);
        var draftId = await CreateDraftAsync(client, companyId, employeeId);

        var firstSubmit = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{draftId}/submit",
            new { companyId, employeeId, leaveRequestId = draftId });
        firstSubmit.EnsureSuccessStatusCode();

        var secondSubmit = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{draftId}/submit",
            new { companyId, employeeId, leaveRequestId = draftId });

        Assert.Equal(HttpStatusCode.BadRequest, secondSubmit.StatusCode);
    }

    private sealed record EmployeePayload(Guid Id, Guid CompanyId, string Status);
    private sealed record PolicyPayload(Guid Id, Guid CompanyId, string Name, bool RequiresApproval);
    private sealed record LeaveRequestDraftPayload(Guid Id, Guid CompanyId, Guid EmployeeId, string Status);
}
