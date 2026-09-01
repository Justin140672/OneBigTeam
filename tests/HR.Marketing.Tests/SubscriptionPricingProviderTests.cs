using System.Net;

using HR.Marketing.Services;
using HR.SharedKernel.Pricing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Marketing.Tests;

public class SubscriptionPricingProviderTests
{
    [Fact]
    public async Task GetAsync_ReturnsDefault_WhenHttpCallFails()
    {
        var provider = BuildProvider(new ThrowingHandler());

        var config = await provider.GetAsync();

        Assert.Equal(SubscriptionPricingConfig.Default, config);
    }

    [Fact]
    public async Task GetAsync_ReturnsDefault_WhenServerReturnsError()
    {
        var provider = BuildProvider(new StatusHandler(HttpStatusCode.InternalServerError));

        var config = await provider.GetAsync();

        Assert.Equal(SubscriptionPricingConfig.Default, config);
    }

    [Fact]
    public async Task GetAsync_ReturnsParsedConfig_WhenFeedIsValid()
    {
        const string json = """
            {"bands":[{"startEmployee":1,"endEmployee":10,"pricePerEmployee":9.00},
            {"startEmployee":11,"endEmployee":null,"pricePerEmployee":4.00}],
            "minimumMonthlyChargeGbp":50.00}
            """;
        var provider = BuildProvider(new JsonHandler(json));

        var config = await provider.GetAsync();

        Assert.Equal(2, config.Bands.Count);
        Assert.Equal(50.00m, config.MinimumMonthlyChargeGbp);
        Assert.Equal(9.00m, config.Bands[0].PricePerEmployee);
    }

    private static SubscriptionPricingProvider BuildProvider(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        return new SubscriptionPricingProvider(
            new StubHttpClientFactory(client),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SubscriptionPricingProvider>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("boom");
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
