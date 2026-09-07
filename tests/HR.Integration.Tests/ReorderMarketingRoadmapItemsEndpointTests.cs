using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ReorderMarketingRoadmapItemsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ReorderMarketingRoadmapItemsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Put_Order_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync("/api/marketing/admin/roadmap/order", new { orderedIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Order_Returns_Forbidden_For_Company_Admin()
    {
        using var client = await MarketingTestHelpers.CompanyAdminClientAsync(_factory);
        var response = await client.PutAsJsonAsync("/api/marketing/admin/roadmap/order", new { orderedIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Order_Assigns_DisplayOrder_By_Index()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);

        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var created = await admin.PostAsJsonAsync("/api/marketing/admin/roadmap",
                MarketingTestHelpers.RoadmapBody($"Item {Guid.NewGuid():N}", displayOrder: 700 + i));
            ids.Add((await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        var reordered = new[] { ids[1], ids[2], ids[0] };
        var response = await admin.PutAsJsonAsync("/api/marketing/admin/roadmap/order", new { orderedIds = reordered });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var map = body.GetProperty("roadmap").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetGuid(), r => r.GetProperty("displayOrder").GetInt32());
        Assert.Equal(0, map[ids[1]]);
        Assert.Equal(1, map[ids[2]]);
        Assert.Equal(2, map[ids[0]]);
    }
}
