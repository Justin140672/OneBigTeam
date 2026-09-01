using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Covers GET /api/companies/{companyId}/audit-log (GetCompanyAuditLog, AUD-05): the
/// employee:manage policy and tenant isolation.
///
/// KNOWN-FAILING DEPENDENCY: the happy-path tests here currently fail in the integration harness
/// with 'column a.actor_type does not exist' because of a broken foreign AUD-04 migration that
/// has not yet been applied/fixed. These tests are written to the intended behaviour and should
/// go green once that migration is corrected — do not delete or weaken them in the meantime.
/// </summary>
[Collection("Integration")]
public class GetCompanyAuditLogEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetCompanyAuditLogEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> ClientAs(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/audit-log");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = await ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/audit-log");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_CompanyAdministrator_Without_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/audit-log");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_An_Invalid_Date_Range()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/audit-log?fromDate=2026-06-01T00:00:00Z&toDate=2026-01-01T00:00:00Z");

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Rejects_An_Out_Of_Range_PageSize()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/audit-log?pageSize=500");

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Returns_Ok_With_Empty_Page_For_HrAdministrator_On_A_Fresh_Company()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/audit-log");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuditLogPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalCount);
        Assert.Equal(1, payload.PageNumber);
    }

    [Fact]
    public async Task Does_Not_Leak_Audit_Events_Across_Companies()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA    = Guid.NewGuid();
        var hrInB    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        // Produce at least one auditable mutation in company A.
        await clientA.PostAsJsonAsync(
            $"/api/companies/{companyA}/public-holidays",
            new { companyId = companyA, date = "2026-12-25", name = "Christmas Day", countryCode = "GB" });

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.GetAsync($"/api/companies/{companyB}/audit-log");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuditLogPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record AuditLogPayload(
        IReadOnlyList<AuditLogItem> Items,
        int TotalCount,
        int PageNumber,
        int PageSize,
        int TotalPages);

    private sealed record AuditLogItem(
        DateTimeOffset OccurredAt,
        string EventType,
        string EntityType,
        Guid EntityId,
        Guid? EmployeeId,
        Guid? ActorUserId,
        string? ActorDisplayName,
        string? Summary);
}
