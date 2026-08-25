using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SaveReportViewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public SaveReportViewEndpointTests(ApiWebApplicationFactory factory)
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

    [Fact]
    public async Task Post_SavedViews_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/reporting/saved-views",
            new { companyId = Guid.NewGuid(), reportId = "employee-directory", name = "My View", filterCriteriaJson = "{}" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_SavedViews_Creates_View()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "My View", filterCriteriaJson = "{}", isDefault = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<SavedViewPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal("employee-directory", payload.ReportId);
        Assert.Equal("My View", payload.Name);
        Assert.False(payload.IsDefault);
    }

    [Fact]
    public async Task Post_SavedViews_Returns_UnprocessableEntity_When_Name_Is_Missing()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = string.Empty, filterCriteriaJson = "{}" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SavedViews_Returns_UnprocessableEntity_When_FilterCriteriaJson_Is_Missing()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "My View", filterCriteriaJson = string.Empty });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SavedViews_Setting_Default_Unsets_Previous_Default()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "First", filterCriteriaJson = "{}", isDefault = true });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "Second", filterCriteriaJson = "{}", isDefault = true });
        second.EnsureSuccessStatusCode();

        var listResponse = await client.GetAsync($"/api/companies/{companyId}/reporting/saved-views/employee-directory");
        var payload = await listResponse.Content.ReadFromJsonAsync<ViewsListPayload>();

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Views.Count);
        var defaultViews = payload.Views.Where(v => v.IsDefault).ToList();
        Assert.Single(defaultViews);
        Assert.Equal("Second", defaultViews[0].Name);
    }

    [Fact]
    public async Task Post_SavedViews_Returns_BadRequest_When_Name_Is_Reserved()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "Standard View", filterCriteriaJson = "{}" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_SavedViews_Returns_BadRequest_When_Name_Is_Reserved_CaseInsensitive_WithWhitespace()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "  standard VIEW  ", filterCriteriaJson = "{}" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_SavedViews_Returns_BadRequest_When_Name_Already_Used_By_Same_User_And_Report()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "My View", filterCriteriaJson = "{}" });
        first.EnsureSuccessStatusCode();

        // Case-insensitive and whitespace-insensitive collision with the same name.
        var duplicate = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "  my view  ", filterCriteriaJson = "{}" });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var listResponse = await client.GetAsync($"/api/companies/{companyId}/reporting/saved-views/employee-directory");
        var payload = await listResponse.Content.ReadFromJsonAsync<ViewsListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Views);
    }

    [Fact]
    public async Task Post_SavedViews_Allows_Same_Name_For_Different_Report()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "My View", filterCriteriaJson = "{}" });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-leavers", name = "My View", filterCriteriaJson = "{}" });

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Post_SavedViews_Allows_Same_Name_For_Different_User()
    {
        var companyId = Guid.NewGuid();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, firstUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, secondUserId, SystemRoles.HrAdministrator);
        using var firstClient = await ClientFor(firstUserId, companyId);
        using var secondClient = await ClientFor(secondUserId, companyId);

        var first = await firstClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "My View", filterCriteriaJson = "{}" });
        first.EnsureSuccessStatusCode();

        var second = await secondClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "My View", filterCriteriaJson = "{}" });

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Post_SavedViews_Returns_BadRequest_For_Unknown_Report_Id()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "not-a-real-report", name = "My View", filterCriteriaJson = "{}" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_SavedViews_Returns_Forbidden_When_Caller_Lacks_Access_To_The_Reports_Gate()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        // HrAdministrator only — no Recruiter role, so the caller lacks reporting:view-recruitment,
        // which "recruitment-pipeline-summary" requires. They still satisfy the endpoint-level
        // "reporting:view" policy (HrAdministrator is one of its OR'd roles), so this exercises the
        // handler's per-report access-gate check, not the endpoint policy.
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "recruitment-pipeline-summary", name = "My View", filterCriteriaJson = "{}" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_SavedViews_Returns_BadRequest_When_FilterCriteriaJson_References_Unsupported_Field()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/reporting/saved-views",
            new { companyId, reportId = "employee-directory", name = "My View", filterCriteriaJson = "{\"TotallyBogusField\":1}" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record SavedViewPayload(Guid Id, string ReportId, string Name, string FilterCriteriaJson, bool IsDefault, DateTimeOffset CreatedAt);

    private sealed record ViewsListPayload(List<SavedViewPayload> Views);
}
