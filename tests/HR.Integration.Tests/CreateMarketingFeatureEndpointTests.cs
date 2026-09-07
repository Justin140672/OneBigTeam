using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateMarketingFeatureEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CreateMarketingFeatureEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_Feature_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/marketing/admin/features",
            MarketingTestHelpers.FeatureBody(MarketingTestHelpers.UniqueSlug()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Feature_Returns_Forbidden_For_Company_Admin()
    {
        using var client = await MarketingTestHelpers.CompanyAdminClientAsync(_factory);
        var response = await client.PostAsJsonAsync("/api/marketing/admin/features",
            MarketingTestHelpers.FeatureBody(MarketingTestHelpers.UniqueSlug()));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Feature_Creates_Feature_As_Unpublished()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var slug = MarketingTestHelpers.UniqueSlug();

        var response = await admin.PostAsJsonAsync("/api/marketing/admin/features",
            MarketingTestHelpers.FeatureBody(slug, title: "Created Feature"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(slug, body.GetProperty("slug").GetString());
        Assert.False(body.GetProperty("isPublished").GetBoolean());
        Assert.Equal("Available", body.GetProperty("deliveryStatus").GetString());
    }

    [Fact]
    public async Task Post_Feature_Returns_Conflict_For_Duplicate_Slug_Case_Insensitive()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var slug = MarketingTestHelpers.UniqueSlug();

        var first = await admin.PostAsJsonAsync("/api/marketing/admin/features", MarketingTestHelpers.FeatureBody(slug));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await admin.PostAsJsonAsync("/api/marketing/admin/features",
            MarketingTestHelpers.FeatureBody(slug.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Post_Feature_Returns_UnprocessableEntity_For_Invalid_Slug()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);

        var response = await admin.PostAsJsonAsync("/api/marketing/admin/features",
            MarketingTestHelpers.FeatureBody("Bad Slug"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
