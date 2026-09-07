using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListMarketingContentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ListMarketingContentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_AdminContent_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/marketing/admin/content");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AdminContent_Returns_Forbidden_For_Authenticated_Company_Admin()
    {
        using var client = await MarketingTestHelpers.CompanyAdminClientAsync(_factory);

        var response = await client.GetAsync("/api/marketing/admin/content");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_AdminContent_Returns_All_Rows_For_Platform_Admin()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var slug = MarketingTestHelpers.UniqueSlug();
        await admin.PostAsJsonAsync("/api/marketing/admin/features", MarketingTestHelpers.FeatureBody(slug));

        var response = await admin.GetAsync("/api/marketing/admin/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var features = body.GetProperty("features").EnumerateArray().ToList();

        // Includes the just-created unpublished feature (admin view is not filtered by publication).
        Assert.Contains(features, f => f.GetProperty("slug").GetString() == slug && !f.GetProperty("isPublished").GetBoolean());
    }
}
