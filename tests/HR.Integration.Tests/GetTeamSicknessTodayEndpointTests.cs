using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// DSH-02: GetTeamSicknessToday derives the caller from <c>ICurrentUser</c> and authorizes the
/// browser-supplied <c>{managerId}</c> route value via
/// <c>SicknessResourceAuthorizer.CanViewManagerTeamAsync</c> (self / manager-above / HR admin),
/// then scopes the results to that manager's entire reporting sub-tree (direct and indirect
/// reports). See specifications/architecture/11-manager-hierarchy-scope.md.
/// Policy-level enforcement of <c>sickness:view-team</c> is covered by SicknessAuthorizationTests;
/// this class covers the resource check and the hierarchy data scope.
/// </summary>
[Collection("Integration")]
public class GetTeamSicknessTodayEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetTeamSicknessTodayEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/team-sickness-today");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Manager_Sees_Active_Sickness_Of_An_Indirect_Report()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var seniorManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(seniorManager, companyId, SystemRoles.Manager);
        var lineManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, lineManager, seniorManager);
        var indirectReport = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, indirectReport, lineManager);

        await CreateActiveSicknessRecordAsync(hrClient, companyId, indirectReport);

        using var seniorClient = await ClientFor(companyId, seniorManager);
        var response = await seniorClient.GetAsync(
            $"/api/companies/{companyId}/employees/{seniorManager}/team-sickness-today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TeamSicknessPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == indirectReport);
    }

    [Fact]
    public async Task SkipLevel_Manager_Gets_Ok_Requesting_A_Subordinate_Managers_Team()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var seniorManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(seniorManager, companyId, SystemRoles.Manager);
        var lineManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, lineManager, seniorManager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, lineManager);

        await CreateActiveSicknessRecordAsync(hrClient, companyId, report);

        using var seniorClient = await ClientFor(companyId, seniorManager);
        var response = await seniorClient.GetAsync(
            $"/api/companies/{companyId}/employees/{lineManager}/team-sickness-today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TeamSicknessPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == report);
    }

    [Fact]
    public async Task Peer_Manager_Gets_Forbidden_Requesting_Another_Managers_Team()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var peerManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(peerManager, companyId, SystemRoles.Manager);

        using var peerClient = await ClientFor(companyId, peerManager);
        var response = await peerClient.GetAsync(
            $"/api/companies/{companyId}/employees/{manager}/team-sickness-today");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unrelated_Employees_Manager_Gets_Forbidden_Requesting_Another_Managers_Team()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);

        var unrelatedManager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(unrelatedManager, companyId, SystemRoles.Manager);
        var unrelatedReport = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, unrelatedReport, unrelatedManager);

        using var unrelatedClient = await ClientFor(companyId, unrelatedManager);
        var response = await unrelatedClient.GetAsync(
            $"/api/companies/{companyId}/employees/{manager}/team-sickness-today");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Ok_Requesting_Any_Managers_Team()
    {
        var companyId = Guid.NewGuid();
        using var hrClient = await HrAdminClientAsync(companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(hrClient, companyId);

        var manager = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignRoleAsync(manager, companyId, SystemRoles.Manager);
        var report = await CreateEmployeeAsync(hrClient, companyId, reference);
        await AssignManagerAsync(hrClient, companyId, report, manager);
        await CreateActiveSicknessRecordAsync(hrClient, companyId, report);

        var response = await hrClient.GetAsync(
            $"/api/companies/{companyId}/employees/{manager}/team-sickness-today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TeamSicknessPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == report);
    }

    // ── Helpers (mirrors SicknessResourceAuthorizationTests) ─────────────────────

    private async Task<HttpClient> HrAdminClientAsync(Guid companyId)
    {
        var userId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        return client;
    }

    private async Task<Guid> CreateEmployeeAsync(
        HttpClient hrClient, Guid companyId, EmployeeReferenceDataSeeder.ReferenceData reference)
    {
        var response = await hrClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, reference, "Test", $"Employee-{Guid.NewGuid():N}",
                $"teamsick.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        await TestRoleSeeder.AssignRoleAsync(_factory, payload!.Id, SystemRoles.Employee, companyId);
        return payload.Id;
    }

    private async Task AssignRoleAsync(Guid userId, Guid companyId, Guid roleId) =>
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private async Task AssignManagerAsync(HttpClient client, Guid companyId, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/manager",
            new { companyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateCategoryAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = $"Category-{Guid.NewGuid():N}",
            displayOrder = 1
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryPayload>())!.Id;
    }

    /// <summary>Creates an open (unclosed) sickness record — its status is Active — for the
    /// employee, so it shows on the team-sickness-today widget.</summary>
    private async Task CreateActiveSicknessRecordAsync(HttpClient hrClient, Guid companyId, Guid employeeId)
    {
        var categoryId = await CreateCategoryAsync(hrClient, companyId);
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var response = await hrClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = startDate.ToString("yyyy-MM-dd"),
                startDayPart = "FullDay"
            });
        response.EnsureSuccessStatusCode();
    }

    private sealed record EmployeePayload(Guid Id);
    private sealed record CategoryPayload(Guid Id);
    private sealed record TeamSicknessPayload(IReadOnlyList<TeamSicknessItemPayload> Items);
    private sealed record TeamSicknessItemPayload(
        Guid Id, Guid EmployeeId, Guid CategoryId, DateOnly StartDate, string EvidenceStatus);
}
