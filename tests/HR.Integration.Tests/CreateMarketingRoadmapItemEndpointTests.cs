using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateMarketingRoadmapItemEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CreateMarketingRoadmapItemEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_Roadmap_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/marketing/admin/roadmap",
            MarketingTestHelpers.RoadmapBody($"Item {Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Roadmap_Returns_Forbidden_For_Company_Admin()
    {
        using var client = await MarketingTestHelpers.CompanyAdminClientAsync(_factory);
        var response = await client.PostAsJsonAsync("/api/marketing/admin/roadmap",
            MarketingTestHelpers.RoadmapBody($"Item {Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Roadmap_Creates_Item_As_Unpublished()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var title = $"Item {Guid.NewGuid():N}";

        var response = await admin.PostAsJsonAsync("/api/marketing/admin/roadmap",
            MarketingTestHelpers.RoadmapBody(title, deliveryStatus: "Planned"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.False(body.GetProperty("isPublished").GetBoolean());
        Assert.Equal("Planned", body.GetProperty("deliveryStatus").GetString());
    }

    [Fact]
    public async Task Post_Roadmap_Returns_UnprocessableEntity_When_Title_Missing()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);

        var response = await admin.PostAsJsonAsync("/api/marketing/admin/roadmap",
            MarketingTestHelpers.RoadmapBody(string.Empty));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
