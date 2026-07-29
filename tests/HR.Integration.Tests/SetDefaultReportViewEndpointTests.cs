using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class SetDefaultReportViewEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public SetDefaultReportViewEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<SavedViewPayload> CreateViewAsync(
        HttpClient client, Guid companyId, string reportId = "employee-directory", string name = "My View", bool isDefault = false)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId, name, filterCriteriaJson = "{}", isDefault });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SavedViewPayload>();
        Assert.NotNull(payload);
        return payload!;
    }

    // SetDefaultReportView's request is entirely route-bound (CompanyId, ViewId) with no JSON body
    // fields, but FastEndpoints still requires a valid Content-Type on PATCH requests.
    private static StringContent EmptyJsonBody() => new("{}", Encoding.UTF8, "application/json");

    [Fact]
    public async Task Patch_SavedViewDefault_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PatchAsync(
            $"/api/companies/{Guid.NewGuid()}/reporting/saved-views/{Guid.NewGuid()}/default", EmptyJsonBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_SavedViewDefault_Sets_View_As_Default_When_Owned_By_Caller()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var view = await CreateViewAsync(client, companyId);

        var response = await client.PatchAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{view.Id}/default", EmptyJsonBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SetDefaultPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.IsDefault);
    }

    [Fact]
    public async Task Patch_SavedViewDefault_Returns_NotFound_When_View_Does_Not_Exist()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.PatchAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{Guid.NewGuid()}/default", EmptyJsonBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetDefaultReportView_WhenNotOwnedByCaller_ReturnsNotFoundAndLeavesOriginalUnchanged()
    {
        var companyId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, ownerUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, otherUserId, SystemRoles.HrAdministrator);

        using var ownerClient = ClientFor(ownerUserId, companyId);
        using var otherClient = ClientFor(otherUserId, companyId);

        var view = await CreateViewAsync(ownerClient, companyId, name: "Owner's View", isDefault: false);

        var response = await otherClient.PatchAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{view.Id}/default", EmptyJsonBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var listResponse = await ownerClient.GetAsync($"/api/companies/{companyId}/reporting/saved-views/employee-directory");
        var payload = await listResponse.Content.ReadFromJsonAsync<ViewsListPayload>();
        Assert.NotNull(payload);
        var reloaded = Assert.Single(payload!.Views);
        Assert.False(reloaded.IsDefault);
    }

    [Fact]
    public async Task Patch_SavedViewDefault_Setting_New_Default_Unsets_Previous_Default()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var first = await CreateViewAsync(client, companyId, name: "First", isDefault: true);
        var second = await CreateViewAsync(client, companyId, name: "Second", isDefault: false);

        var response = await client.PatchAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{second.Id}/default", EmptyJsonBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listResponse = await client.GetAsync($"/api/companies/{companyId}/reporting/saved-views/employee-directory");
        var payload = await listResponse.Content.ReadFromJsonAsync<ViewsListPayload>();

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Views.Count);
        var defaultViews = payload.Views.Where(v => v.IsDefault).ToList();
        Assert.Single(defaultViews);
        Assert.Equal(second.Id, defaultViews[0].Id);
        Assert.NotEqual(first.Id, defaultViews[0].Id);
    }

    private sealed record SavedViewPayload(Guid Id, string ReportId, string Name, string FilterCriteriaJson, bool IsDefault, DateTimeOffset CreatedAt);

    private sealed record SetDefaultPayload(Guid Id, bool IsDefault);

    private sealed record ViewsListPayload(List<SavedViewPayload> Views);
}
