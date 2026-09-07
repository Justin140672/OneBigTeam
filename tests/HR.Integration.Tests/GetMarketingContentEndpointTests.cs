using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetMarketingContentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetMarketingContentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Content_Is_Anonymous_And_Returns_Seeded_Published_Features()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/marketing/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var slugs = body.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("slug").GetString())
            .ToList();
        Assert.Contains("employee-management", slugs);
        Assert.Contains("reporting", slugs);
    }

    [Fact]
    public async Task Get_Content_Excludes_Unpublished_Feature_Even_When_Delivery_Status_Is_Available()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var slug = MarketingTestHelpers.UniqueSlug();

        var created = await admin.PostAsJsonAsync("/api/marketing/admin/features",
            MarketingTestHelpers.FeatureBody(slug, deliveryStatus: "Available"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var anon = _factory.CreateClient();
        var body = await (await anon.GetAsync("/api/marketing/content")).Content.ReadFromJsonAsync<JsonElement>();

        var slugs = body.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("slug").GetString());
        Assert.DoesNotContain(slug, slugs);
    }

    [Fact]
    public async Task Get_Content_Includes_Feature_After_It_Is_Published()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);
        var slug = MarketingTestHelpers.UniqueSlug();

        var created = await admin.PostAsJsonAsync("/api/marketing/admin/features", MarketingTestHelpers.FeatureBody(slug));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var publish = await admin.PutAsJsonAsync($"/api/marketing/admin/features/{id}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        using var anon = _factory.CreateClient();
        var body = await (await anon.GetAsync("/api/marketing/content")).Content.ReadFromJsonAsync<JsonElement>();

        var slugs = body.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("slug").GetString());
        Assert.Contains(slug, slugs);
    }

    [Fact]
    public async Task Get_Content_Features_Are_Ordered_By_DisplayOrder()
    {
        using var admin = await MarketingTestHelpers.PlatformAdminClientAsync(_factory);

        // High display orders keep these after the seeded features (indexes 0-6).
        var low = MarketingTestHelpers.UniqueSlug();
        var mid = MarketingTestHelpers.UniqueSlug();
        var high = MarketingTestHelpers.UniqueSlug();

        foreach (var (slug, order) in new[] { (high, 903), (low, 901), (mid, 902) })
        {
            var created = await admin.PostAsJsonAsync("/api/marketing/admin/features",
                MarketingTestHelpers.FeatureBody(slug, displayOrder: order));
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await admin.PutAsJsonAsync($"/api/marketing/admin/features/{id}/publication", new { isPublished = true });
        }

        using var anon = _factory.CreateClient();
        var body = await (await anon.GetAsync("/api/marketing/content")).Content.ReadFromJsonAsync<JsonElement>();

        var slugs = body.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("slug").GetString())
            .Where(s => s == low || s == mid || s == high)
            .ToList();

        Assert.Equal(new[] { low, mid, high }, slugs);
    }
}
