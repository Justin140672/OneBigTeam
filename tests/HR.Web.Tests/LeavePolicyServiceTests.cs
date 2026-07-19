using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class LeavePolicyServiceTests
{
    private static IHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    [Fact]
    public async Task ListLeavePoliciesAsync_Requests_ActiveOnly_When_Requested()
    {
        var handler = new CapturingHandler();
        var factory = BuildFactory(handler);
        var service = new LeavePolicyService(factory);

        await service.ListLeavePoliciesAsync(Guid.NewGuid(), activeOnly: true);

        Assert.Contains("activeOnly=true", handler.LastRequestUri?.Query);
    }

    [Fact]
    public async Task ListLeavePoliciesAsync_Does_Not_Append_ActiveOnly_By_Default()
    {
        var handler = new CapturingHandler();
        var factory = BuildFactory(handler);
        var service = new LeavePolicyService(factory);

        await service.ListLeavePoliciesAsync(Guid.NewGuid());

        Assert.DoesNotContain("activeOnly", handler.LastRequestUri?.Query ?? string.Empty);
    }

    [Fact]
    public async Task ListLeavePoliciesAsync_Returns_Null_On_Network_Failure()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new LeavePolicyService(factory);

        var result = await service.ListLeavePoliciesAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLeavePolicyAsync_Returns_Response_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var response = new GetLeavePolicyResponse(policyId, companyId, "Standard", "Default policy", 5, false, true, false, DateTimeOffset.UtcNow);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new LeavePolicyService(factory);

        var result = await service.GetLeavePolicyAsync(companyId, policyId);

        Assert.NotNull(result);
        Assert.Equal("Standard", result.Name);
        Assert.Equal(5, result.CarryOverDays);
    }

    [Fact]
    public async Task GetLeavePolicyAsync_Returns_Null_On_Network_Failure()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new LeavePolicyService(factory);

        var result = await service.GetLeavePolicyAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ListLeavePoliciesResponse([]))
            };
            return Task.FromResult(response);
        }
    }

    private sealed class JsonResponseHandler(HttpStatusCode statusCode, object payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(payload) };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network failure");
    }
}
