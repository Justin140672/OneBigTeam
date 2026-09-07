using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateMarketingRoadmapItemEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public UpdateMarketingRoadmapItemEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Admin, Guid Id)> SeedItemAsync()
    {
        var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var created = await admin.PostAsJsonAsync("/api/marketing/admin/roadmap",
            MarketingTestHelpers.RoadmapBody($"Item {Guid.NewGuid():N}"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return (admin, id);
    }

    [Fact]
    public async Task Put_Roadmap_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync($"/api/marketing/admin/roadmap/{Guid.NewGuid()}",
            MarketingTestHelpers.RoadmapBody("Renamed"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Roadmap_Returns_Forbidden_For_Company_Admin()
    {
        using var client = await MarketingTestHelpers.CompanyAdminClientAsync(_factory);
        var response = await client.PutAsJsonAsync($"/api/marketing/admin/roadmap/{Guid.NewGuid()}",
            MarketingTestHelpers.RoadmapBody("Renamed"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Roadmap_Updates_Fields()
    {
        var (admin, id) = await SeedItemAsync();
        using var _admin = admin;

        var response = await admin.PutAsJsonAsync($"/api/marketing/admin/roadmap/{id}",
            MarketingTestHelpers.RoadmapBody("Renamed item", displayOrder: 7, deliveryStatus: "Planned"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed item", body.GetProperty("title").GetString());
        Assert.Equal(7, body.GetProperty("displayOrder").GetInt32());
        Assert.Equal("Planned", body.GetProperty("deliveryStatus").GetString());
    }

    [Fact]
    public async Task Put_Roadmap_Returns_NotFound_For_Unknown_Id()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var response = await admin.PutAsJsonAsync($"/api/marketing/admin/roadmap/{Guid.NewGuid()}",
            MarketingTestHelpers.RoadmapBody("Renamed"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Roadmap_Returns_UnprocessableEntity_When_Description_Missing()
    {
        var (admin, id) = await SeedItemAsync();
        using var _admin = admin;

        var response = await admin.PutAsJsonAsync($"/api/marketing/admin/roadmap/{id}", new
        {
            title = "Has title",
            description = "",
            iconName = "chart-line",
            deliveryStatus = "ComingSoon",
            displayOrder = 0,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
