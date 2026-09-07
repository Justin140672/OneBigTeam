using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SetMarketingRoadmapItemPublicationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public SetMarketingRoadmapItemPublicationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Put_Publication_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync($"/api/marketing/admin/roadmap/{Guid.NewGuid()}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Publication_Returns_Forbidden_For_Company_Admin()
    {
        using var client = await MarketingTestHelpers.CompanyAdminClientAsync(_factory);
        var response = await client.PutAsJsonAsync($"/api/marketing/admin/roadmap/{Guid.NewGuid()}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Publication_Toggles_Published_State()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var created = await admin.PostAsJsonAsync("/api/marketing/admin/roadmap",
            MarketingTestHelpers.RoadmapBody($"Item {Guid.NewGuid():N}"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var publish = await admin.PutAsJsonAsync($"/api/marketing/admin/roadmap/{id}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.True((await publish.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isPublished").GetBoolean());

        var unpublish = await admin.PutAsJsonAsync($"/api/marketing/admin/roadmap/{id}/publication", new { isPublished = false });
        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        Assert.False((await unpublish.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isPublished").GetBoolean());
    }

    [Fact]
    public async Task Put_Publication_Returns_NotFound_For_Unknown_Id()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var response = await admin.PutAsJsonAsync($"/api/marketing/admin/roadmap/{Guid.NewGuid()}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
