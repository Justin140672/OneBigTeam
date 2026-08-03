using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RenameReportViewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public RenameReportViewEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Patch_SavedView_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PatchAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/reporting/saved-views/{Guid.NewGuid()}",
            new { companyId = Guid.NewGuid(), viewId = Guid.NewGuid(), name = "New Name" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_SavedView_Renames_View_When_Owned_By_Caller()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var view = await CreateViewAsync(client, companyId);

        var response = await client.PatchAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{view.Id}",
            new { companyId, viewId = view.Id, name = "Renamed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RenamePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Renamed", payload!.Name);
    }

    [Fact]
    public async Task Patch_SavedView_Returns_NotFound_When_View_Does_Not_Exist()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.PatchAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{Guid.NewGuid()}",
            new { companyId, viewId = Guid.NewGuid(), name = "New Name" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_SavedView_Returns_UnprocessableEntity_When_Name_Is_Empty()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var view = await CreateViewAsync(client, companyId);

        var response = await client.PatchAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{view.Id}",
            new { companyId, viewId = view.Id, name = string.Empty });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RenameReportView_WhenNotOwnedByCaller_ReturnsNotFoundAndLeavesOriginalUnchanged()
    {
        var companyId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, ownerUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, otherUserId, SystemRoles.HrAdministrator);

        using var ownerClient = ClientFor(ownerUserId, companyId);
        using var otherClient = ClientFor(otherUserId, companyId);

        var view = await CreateViewAsync(ownerClient, companyId, name: "Owner's View");

        var response = await otherClient.PatchAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{view.Id}",
            new { companyId, viewId = view.Id, name = "Hijacked" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var listResponse = await ownerClient.GetAsync($"/api/companies/{companyId}/reporting/saved-views/employee-directory");
        var payload = await listResponse.Content.ReadFromJsonAsync<ViewsListPayload>();
        Assert.NotNull(payload);
        var reloaded = Assert.Single(payload!.Views);
        Assert.Equal("Owner's View", reloaded.Name);
    }

    [Fact]
    public async Task Patch_SavedView_Returns_BadRequest_When_Name_Is_Reserved()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var view = await CreateViewAsync(client, companyId);

        var response = await client.PatchAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{view.Id}",
            new { companyId, viewId = view.Id, name = "Standard View" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_SavedView_Returns_BadRequest_When_Name_Already_Used_By_Another_View()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        await CreateViewAsync(client, companyId, name: "First");
        var second = await CreateViewAsync(client, companyId, name: "Second");

        // Case-insensitive and whitespace-insensitive collision with the other view's name.
        var response = await client.PatchAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{second.Id}",
            new { companyId, viewId = second.Id, name = "  first  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var listResponse = await client.GetAsync($"/api/companies/{companyId}/reporting/saved-views/employee-directory");
        var payload = await listResponse.Content.ReadFromJsonAsync<ViewsListPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Views, v => v.Id == second.Id && v.Name == "Second");
    }

    [Fact]
    public async Task Patch_SavedView_Allows_Renaming_To_Its_Own_Current_Name()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var view = await CreateViewAsync(client, companyId, name: "My View");

        var response = await client.PatchAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views/{view.Id}",
            new { companyId, viewId = view.Id, name = "My View" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record SavedViewPayload(Guid Id, string ReportId, string Name, string FilterCriteriaJson, bool IsDefault, DateTimeOffset CreatedAt);

    private sealed record RenamePayload(Guid Id, string Name);

    private sealed record ViewsListPayload(List<SavedViewPayload> Views);
}
