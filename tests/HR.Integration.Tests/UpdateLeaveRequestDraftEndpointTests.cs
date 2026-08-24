using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateLeaveRequestDraftEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("cccccccc-0000-0000-0000-000000000002");

    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnnualLeaveTypeId = Guid.Parse("A0000000-0000-0000-0000-000000000001");

    public UpdateLeaveRequestDraftEndpointTests(ApiWebApplicationFactory factory)
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
                lastName = "Editor",
                workEmail = $"draft.editor.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"DE-{Guid.NewGuid():N}",
                employmentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002")
            });
        employeeResponse.EnsureSuccessStatusCode();
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        return (client, employee!.Id);
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
                reason = "Original"
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestDraftPayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task Put_Draft_Returns_Ok_With_Updated_Fields()
    {
        var (client, employeeId) = await SetupEmployeeAsync();
        var companyId = SeededCompanyId;
        var draftId = await CreateDraftAsync(client, companyId, employeeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{draftId}/draft",
            new
            {
                companyId,
                employeeId,
                leaveRequestId = draftId,
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-08-10",
                startPart = "FullDay",
                endDate = "2026-08-11",
                endPart = "FullDay",
                reason = "Updated"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestDraftPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Updated", payload!.Reason);
    }

    [Fact]
    public async Task Put_Draft_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = SeededCompanyId;
        var employeeId = Guid.NewGuid();
        var draftId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{draftId}/draft",
            new
            {
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-08-10",
                startPart = "FullDay",
                endDate = "2026-08-11",
                endPart = "FullDay"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Draft_Returns_NotFound_For_Unknown_Id()
    {
        var (client, employeeId) = await SetupEmployeeAsync();
        var companyId = SeededCompanyId;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{Guid.NewGuid()}/draft",
            new
            {
                companyId,
                employeeId,
                leaveRequestId = Guid.NewGuid(),
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-08-10",
                startPart = "FullDay",
                endDate = "2026-08-11",
                endPart = "FullDay"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Draft_Returns_BadRequest_When_Target_Request_Is_Not_A_Draft()
    {
        var (client, employeeId) = await SetupEmployeeAsync();
        var companyId = SeededCompanyId;
        var draftId = await CreateDraftAsync(client, companyId, employeeId);

        // Assign a policy that allows negative balance so submission succeeds without a
        // separately-seeded LeaveBalance row.
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

        var submitResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{draftId}/submit",
            new { companyId, employeeId, leaveRequestId = draftId });
        submitResponse.EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/{draftId}/draft",
            new
            {
                companyId,
                employeeId,
                leaveRequestId = draftId,
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-08-10",
                startPart = "FullDay",
                endDate = "2026-08-11",
                endPart = "FullDay"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record EmployeePayload(Guid Id, Guid CompanyId, string Status);
    private sealed record PolicyPayload(Guid Id, Guid CompanyId, string Name, bool AllowNegativeBalance);
    private sealed record LeaveRequestDraftPayload(Guid Id, Guid CompanyId, Guid EmployeeId, string Status, string? Reason);
}
