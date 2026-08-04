using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DeleteReportViewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public DeleteReportViewEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<SavedViewPayload> CreateViewAsync(HttpClient client, Guid companyId, string reportId = "employee-directory", string name = "My View")
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId, name, filterCriteriaJson = "{}", isDefault = false });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SavedViewPayload>();
        Assert.NotNull(payload);
        return payload!;
    }

    [Fact]
    public async Task Delete_SavedView_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/reporting/saved-views/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_SavedView_Deletes_View_When_Owned_By_Caller()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var view = await CreateViewAsync(client, companyId);

        var response = await client.DeleteAsync($"/api/companies/{companyId}/reporting/saved-views/{view.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listResponse = await client.GetAsync($"/api/companies/{companyId}/reporting/saved-views/employee-directory");
        var payload = await listResponse.Content.ReadFromJsonAsync<ViewsListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Views);
    }

    [Fact]
    public async Task Delete_SavedView_Returns_NotFound_When_View_Does_Not_Exist()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.DeleteAsync($"/api/companies/{companyId}/reporting/saved-views/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteReportView_WhenNotOwnedByCaller_ReturnsNotFoundAndLeavesOriginalUnchanged()
    {
        var companyId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, ownerUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, otherUserId, SystemRoles.HrAdministrator);

        using var ownerClient = await ClientFor(ownerUserId, companyId);
        using var otherClient = await ClientFor(otherUserId, companyId);

        var view = await CreateViewAsync(ownerClient, companyId, name: "Owner's View");

        var response = await otherClient.DeleteAsync($"/api/companies/{companyId}/reporting/saved-views/{view.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var listResponse = await ownerClient.GetAsync($"/api/companies/{companyId}/reporting/saved-views/employee-directory");
        var payload = await listResponse.Content.ReadFromJsonAsync<ViewsListPayload>();
        Assert.NotNull(payload);
        var reloaded = Assert.Single(payload!.Views);
        Assert.Equal("Owner's View", reloaded.Name);
    }

    private sealed record SavedViewPayload(Guid Id, string ReportId, string Name, string FilterCriteriaJson, bool IsDefault, DateTimeOffset CreatedAt);

    private sealed record ViewsListPayload(List<SavedViewPayload> Views);
}
