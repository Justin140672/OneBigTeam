using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetReportViewsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetReportViewsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
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

    [Fact]
    public async Task Get_SavedViews_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/reporting/saved-views/employee-directory");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SavedViews_Returns_Empty_When_None_Saved()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/saved-views/employee-directory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ViewsListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Views);
    }

    [Fact]
    public async Task Get_SavedViews_Returns_Only_Callers_Views_For_The_Requested_Report()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, otherUserId, SystemRoles.HrAdministrator);

        using var client = await ClientFor(userId, companyId);
        using var otherClient = await ClientFor(otherUserId, companyId);

        await CreateViewAsync(client, companyId, reportId: "employee-directory", name: "Mine");
        await CreateViewAsync(client, companyId, reportId: "sickness-report", name: "Different Report");
        await CreateViewAsync(otherClient, companyId, reportId: "employee-directory", name: "Someone Else's");

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/saved-views/employee-directory");
        var payload = await response.Content.ReadFromJsonAsync<ViewsListPayload>();

        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Views);
        Assert.Equal("Mine", item.Name);
    }

    [Fact]
    public async Task Get_SavedViews_After_Setting_Second_Default_Only_Second_Is_Default()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var first = await CreateViewAsync(client, companyId, name: "First", isDefault: true);
        var second = await CreateViewAsync(client, companyId, name: "Second", isDefault: false);

        var setDefaultResponse = await client.PatchAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{second.Id}/default",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, setDefaultResponse.StatusCode);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/saved-views/employee-directory");
        var payload = await response.Content.ReadFromJsonAsync<ViewsListPayload>();

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Views.Count);
        var firstReloaded = payload.Views.Single(v => v.Id == first.Id);
        var secondReloaded = payload.Views.Single(v => v.Id == second.Id);
        Assert.False(firstReloaded.IsDefault);
        Assert.True(secondReloaded.IsDefault);
    }

    private sealed record SavedViewPayload(Guid Id, string ReportId, string Name, string FilterCriteriaJson, bool IsDefault, DateTimeOffset CreatedAt);

    private sealed record ViewsListPayload(List<SavedViewPayload> Views);
}
