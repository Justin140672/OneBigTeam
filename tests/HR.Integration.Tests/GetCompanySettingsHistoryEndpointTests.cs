using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// SET-02: company-settings history is reached via GET .../settings/history, gated by the same
/// "company:manage" (CompanyAdministrator-only) policy as UpdateCompanySettings — see
/// UpdateCompanySettingsEndpointTests and IdentityModule.AddRolePolicies for the policy definition.
/// </summary>
[Collection("Integration")]
public class GetCompanySettingsHistoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid CompanyAdminUserId = new("eeeeeeee-2222-0000-0000-000000000001");
    private static readonly Guid HrAdminOnlyUserId = new("eeeeeeee-2222-0000-0000-000000000002");

    public GetCompanySettingsHistoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUserId, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminOnlyUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminOnlyUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid tenantId, bool ensureActiveSubscription = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, tenantId, ensureActiveSubscription);
        return client;
    }

    [Fact]
    public async Task Get_History_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/settings/history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_History_Returns_Updated_Entry_With_Actor_Attribution_For_CompanyAdministrator()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminUserId, tenantId);

        var updateResponse = await client.PutAsJsonAsync($"/api/companies/{tenantId}/settings", new
        {
            timeZone = "Europe/London",
            locale = "en-GB",
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var historyResponse = await client.GetAsync($"/api/companies/{tenantId}/settings/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        var payload = await historyResponse.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal("company-settings", item.Category);
        Assert.Equal(CompanyAdminUserId, item.ActorUserId);
        Assert.Equal($"testuser-{CompanyAdminUserId:N}@test.internal", item.ActorEmail);
        Assert.Contains("Europe/London", item.NewValueJson);
    }

    [Fact]
    public async Task Get_History_Returns_Forbidden_For_HrAdministrator_Only_Role()
    {
        // company:manage is CompanyAdministrator-only — an HR Administrator without that role
        // must not be able to view company-settings history.
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminOnlyUserId, tenantId);

        var response = await client.GetAsync($"/api/companies/{tenantId}/settings/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_History_Returns_Forbidden_For_Foreign_Company_Id()
    {
        // The route companyId must match the caller's resolved tenant — a Company Administrator
        // cannot view another company's settings history by simply changing the route id.
        var tenantId = Guid.NewGuid();
        var foreignCompanyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminUserId, tenantId);

        var response = await client.GetAsync($"/api/companies/{foreignCompanyId}/settings/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record HistoryPayload(
        IReadOnlyList<HistoryItemPayload> Items,
        int TotalCount,
        int PageNumber,
        int PageSize,
        int TotalPages);

    private sealed record HistoryItemPayload(
        DateTimeOffset OccurredAt,
        string Category,
        Guid? ActorUserId,
        string? ActorEmail,
        string? PreviousValueJson,
        string? NewValueJson);
}
