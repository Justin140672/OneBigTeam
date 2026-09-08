using System.Net;

using HR.Marketing.Services;

using Microsoft.Extensions.Caching.Memory;

namespace HR.Marketing.Tests;

public class MarketingContentServiceTests
{
    private static MarketingContentService CreateService(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task GetContentAsync_WhenEndpointUnavailable_FallsBackToSeedCatalog()
    {
        var service = CreateService(new StubHandler(_ => throw new HttpRequestException("boom")));

        var content = await service.GetContentAsync();

        Assert.Equal(FeatureCatalog.All.Count, content.Features.Count);
        Assert.Equal(UpcomingFeatureCatalog.All.Count, content.Roadmap.Count);
        Assert.Contains(content.Features, f => f.Slug == "employee-management");
        // Fallback must never expose drafts — seed content is treated as published/available.
        Assert.All(content.Features, f => Assert.Equal("Available", f.DeliveryStatus));
    }

    [Fact]
    public async Task GetContentAsync_WhenEndpointReturns500_FallsBackWithoutThrowing()
    {
        var service = CreateService(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var content = await service.GetContentAsync();

        Assert.NotEmpty(content.Features);
    }

    [Fact]
    public async Task GetContentAsync_ParsesPublishedContentFromEndpoint()
    {
        const string json = """
        {
          "product": { "name": "One Big Team", "tagline": "People ops" },
          "features": [
            { "slug": "alpha", "iconName": "users", "title": "Alpha", "summary": "s", "intro": "i",
              "detailedContent": null, "benefits": ["b1","b2"], "youTubeId": "abc", "deliveryStatus": "Available" }
          ],
          "roadmap": [
            { "title": "Beta", "description": "d", "iconName": "chart-line", "deliveryStatus": "Planned" }
          ]
        }
        """;
        var service = CreateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        }));

        var content = await service.GetContentAsync();

        Assert.Equal("People ops", content.ProductTagline);
        var feature = Assert.Single(content.Features);
        Assert.Equal("alpha", feature.Slug);
        Assert.Equal(["b1", "b2"], feature.Benefits);
        var roadmap = Assert.Single(content.Roadmap);
        Assert.Equal("Planned", roadmap.DeliveryStatus);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("http://localhost/") };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
