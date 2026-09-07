using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateMarketingFeatureEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public UpdateMarketingFeatureEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Admin, Guid Id, string Slug)> SeedFeatureAsync()
    {
        var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var slug = MarketingTestHelpers.UniqueSlug();
        var created = await admin.PostAsJsonAsync("/api/marketing/admin/features", MarketingTestHelpers.FeatureBody(slug));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return (admin, id, slug);
    }

    [Fact]
    public async Task Put_Feature_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync($"/api/marketing/admin/features/{Guid.NewGuid()}",
            MarketingTestHelpers.FeatureBody(MarketingTestHelpers.UniqueSlug()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Feature_Returns_Forbidden_For_Company_Admin()
    {
        using var client = await MarketingTestHelpers.CompanyAdminClientAsync(_factory);
        var response = await client.PutAsJsonAsync($"/api/marketing/admin/features/{Guid.NewGuid()}",
            MarketingTestHelpers.FeatureBody(MarketingTestHelpers.UniqueSlug()));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Feature_Updates_Fields()
    {
        var (admin, id, _) = await SeedFeatureAsync();
        using var _admin = admin;
        var newSlug = MarketingTestHelpers.UniqueSlug();

        var response = await admin.PutAsJsonAsync($"/api/marketing/admin/features/{id}",
            MarketingTestHelpers.FeatureBody(newSlug, title: "Renamed", displayOrder: 5, deliveryStatus: "ComingSoon"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(newSlug, body.GetProperty("slug").GetString());
        Assert.Equal("Renamed", body.GetProperty("title").GetString());
        Assert.Equal(5, body.GetProperty("displayOrder").GetInt32());
        Assert.Equal("ComingSoon", body.GetProperty("deliveryStatus").GetString());
    }

    [Fact]
    public async Task Put_Feature_Returns_NotFound_For_Unknown_Id()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var response = await admin.PutAsJsonAsync($"/api/marketing/admin/features/{Guid.NewGuid()}",
            MarketingTestHelpers.FeatureBody(MarketingTestHelpers.UniqueSlug()));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Feature_Returns_Conflict_For_Slug_Owned_By_Another_Feature()
    {
        var (admin, id, _) = await SeedFeatureAsync();
        using var _admin = admin;
        var otherSlug = MarketingTestHelpers.UniqueSlug();
        await admin.PostAsJsonAsync("/api/marketing/admin/features", MarketingTestHelpers.FeatureBody(otherSlug));

        var response = await admin.PutAsJsonAsync($"/api/marketing/admin/features/{id}",
            MarketingTestHelpers.FeatureBody(otherSlug.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_Feature_Returns_UnprocessableEntity_For_Invalid_Slug()
    {
        var (admin, id, _) = await SeedFeatureAsync();
        using var _admin = admin;

        var response = await admin.PutAsJsonAsync($"/api/marketing/admin/features/{id}",
            MarketingTestHelpers.FeatureBody("Bad Slug"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
