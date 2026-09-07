using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ReorderMarketingFeaturesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ReorderMarketingFeaturesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Put_Order_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync("/api/marketing/admin/features/order", new { orderedIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Order_Returns_Forbidden_For_Company_Admin()
    {
        using var client = await MarketingTestHelpers.CompanyAdminClientAsync(_factory);
        var response = await client.PutAsJsonAsync("/api/marketing/admin/features/order", new { orderedIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Order_Assigns_DisplayOrder_By_Index()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);

        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var created = await admin.PostAsJsonAsync("/api/marketing/admin/features",
                MarketingTestHelpers.FeatureBody(MarketingTestHelpers.UniqueSlug(), displayOrder: 800 + i));
            ids.Add((await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        var reordered = new[] { ids[2], ids[0], ids[1] };
        var response = await admin.PutAsJsonAsync("/api/marketing/admin/features/order", new { orderedIds = reordered });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var map = body.GetProperty("features").EnumerateArray()
            .ToDictionary(f => f.GetProperty("id").GetGuid(), f => f.GetProperty("displayOrder").GetInt32());
        Assert.Equal(0, map[ids[2]]);
        Assert.Equal(1, map[ids[0]]);
        Assert.Equal(2, map[ids[1]]);
    }
}
