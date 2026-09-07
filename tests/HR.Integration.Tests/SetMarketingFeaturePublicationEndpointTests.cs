using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SetMarketingFeaturePublicationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public SetMarketingFeaturePublicationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Admin, Guid Id)> SeedFeatureAsync()
    {
        var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var created = await admin.PostAsJsonAsync("/api/marketing/admin/features",
            MarketingTestHelpers.FeatureBody(MarketingTestHelpers.UniqueSlug()));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return (admin, id);
    }

    [Fact]
    public async Task Put_Publication_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync($"/api/marketing/admin/features/{Guid.NewGuid()}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Publication_Returns_Forbidden_For_Company_Admin()
    {
        using var client = await MarketingTestHelpers.CompanyAdminClientAsync(_factory);
        var response = await client.PutAsJsonAsync($"/api/marketing/admin/features/{Guid.NewGuid()}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Publication_Toggles_Published_State()
    {
        var (admin, id) = await SeedFeatureAsync();
        using var _admin = admin;

        var publish = await admin.PutAsJsonAsync($"/api/marketing/admin/features/{id}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.True((await publish.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isPublished").GetBoolean());

        var unpublish = await admin.PutAsJsonAsync($"/api/marketing/admin/features/{id}/publication", new { isPublished = false });
        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        Assert.False((await unpublish.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isPublished").GetBoolean());
    }

    [Fact]
    public async Task Put_Publication_Returns_NotFound_For_Unknown_Id()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var response = await admin.PutAsJsonAsync($"/api/marketing/admin/features/{Guid.NewGuid()}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
