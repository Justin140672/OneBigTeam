using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetTeamOnboardingEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = Guid.Parse("ee000002-0000-0000-0000-000000000001");

    public GetTeamOnboardingEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_TeamOnboarding_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/team-onboarding");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_TeamOnboarding_Returns_Empty_When_Manager_Has_No_Direct_Reports()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var manager = await CreateEmployeeAsync(client, companyId, "Solo", "Manager");

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{manager.Id}/team-onboarding");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_TeamOnboarding_Returns_Active_Onboarding_Plan_For_Direct_Report()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var manager = await CreateEmployeeAsync(client, companyId, "Alice", "Manager");
        var report = await CreateEmployeeAsync(client, companyId, "Bob", "Reporter");

        await AssignManagerAsync(client, companyId, report.Id, manager.Id);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{manager.Id}/team-onboarding");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(report.Id, item.EmployeeId);
        Assert.Equal("Bob Reporter", item.EmployeeName);
        Assert.Equal("NotStarted", item.PlanStatus);
        Assert.True(item.TotalTasks > 0);
        Assert.Equal(0, item.CompletedTasks);
        Assert.Equal(0, item.PercentComplete);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<EmployeePayload> CreateEmployeeAsync(
        HttpClient client, Guid companyId, string firstName, string lastName)
    {
        var (departmentId, locationId, positionProfileId, employmentTypeId) =
            await CreateEmployeeReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName,
                lastName,
                workEmail = $"{firstName.ToLower()}.{lastName.ToLower()}.{Guid.NewGuid():N}@example.com",
                startDate = "2026-07-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"TON-{Guid.NewGuid():N}",
                employmentTypeId,
                departmentId,
                locationId,
                positionProfileId
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeePayload>())!;
    }

    private async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)>
        CreateEmployeeReferenceDataAsync(HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<EmployeePayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<EmployeePayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<EmployeePayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"RefLeavePolicy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<EmployeePayload>())!.Id;

        var ppResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}", defaultLeavePolicyId });
        ppResp.EnsureSuccessStatusCode();
        var positionProfileId = (await ppResp.Content.ReadFromJsonAsync<EmployeePayload>())!.Id;

        var etResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResp.Content.ReadFromJsonAsync<EmployeePayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private async Task AssignManagerAsync(HttpClient client, Guid companyId, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/manager",
            new { companyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    private sealed record EmployeePayload(Guid Id);

    private sealed record ListPayload(IReadOnlyList<TeamOnboardingItemPayload> Items);

    private sealed record TeamOnboardingItemPayload(
        Guid EmployeeId,
        string EmployeeName,
        string PlanStatus,
        DateOnly StartDate,
        int TotalTasks,
        int CompletedTasks,
        int PercentComplete);
}
