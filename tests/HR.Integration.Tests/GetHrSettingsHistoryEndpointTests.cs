using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// SET-02 counterpart to GetCompanySettingsHistoryEndpointTests — gated by "hr-settings:manage"
/// (HrAdministrator-only), the mirror-image restriction to company:manage. See
/// UpdateHrSettingsEndpointTests and IdentityModule.AddRolePolicies for the policy definition.
/// </summary>
[Collection("Integration")]
public class GetHrSettingsHistoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("eeeeeeee-3333-0000-0000-000000000001");
    private static readonly Guid CompanyAdminOnlyUserId = new("eeeeeeee-3333-0000-0000-000000000002");

    public GetHrSettingsHistoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminOnlyUserId, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminOnlyUserId, SystemRoles.Employee);
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

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/hr-settings/history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_History_Returns_Updated_Entry_With_Actor_Attribution_For_HrAdministrator()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var updateResponse = await client.PutAsJsonAsync($"/api/companies/{tenantId}/hr-settings", new
        {
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 4,
            defaultHolidayAllowance = 28,
            probationMonths = 3,
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var historyResponse = await client.GetAsync($"/api/companies/{tenantId}/hr-settings/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        var payload = await historyResponse.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal("hr-settings", item.Category);
        Assert.Equal(HrAdminUserId, item.ActorUserId);
        Assert.Equal($"testuser-{HrAdminUserId:N}@test.internal", item.ActorEmail);
        Assert.Contains("28", item.NewValueJson);
    }

    [Fact]
    public async Task Get_History_Returns_Forbidden_For_CompanyAdministrator_Only_Role()
    {
        // hr-settings:manage is HrAdministrator-only — a Company Administrator without HR admin
        // rights must not be able to view HR-settings history.
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminOnlyUserId, tenantId);

        var response = await client.GetAsync($"/api/companies/{tenantId}/hr-settings/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_History_Returns_Forbidden_For_Foreign_Company_Id()
    {
        var tenantId = Guid.NewGuid();
        var foreignCompanyId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.GetAsync($"/api/companies/{foreignCompanyId}/hr-settings/history");

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
