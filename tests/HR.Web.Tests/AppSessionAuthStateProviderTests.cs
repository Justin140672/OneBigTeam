using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class AppSessionAuthStateProviderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuthenticationStateAsync_ReturnsUnauthenticated_When_ApiMe_Returns401()
    {
        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.Unauthorized));
        var provider = new AppSessionAuthStateProvider(factory);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task GetAuthenticationStateAsync_ReturnsUnauthenticated_When_ApiMe_Returns403()
    {
        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.Forbidden));
        var provider = new AppSessionAuthStateProvider(factory);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task GetAuthenticationStateAsync_ReturnsAuthenticated_When_ApiMe_ReturnsValidUser()
    {
        var userId    = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var me        = new MeResponse(userId, companyId, "alice@example.com", [], [], false, false, false, false, true);

        var factory  = BuildFactory(new JsonResponseHandler(me));
        var provider = new AppSessionAuthStateProvider(factory);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity?.IsAuthenticated);
        Assert.Equal(userId.ToString(),    state.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("alice@example.com",  state.User.FindFirstValue(ClaimTypes.Email));
        Assert.Equal(companyId.ToString(), state.User.FindFirstValue("company_id"));
    }

    [Fact]
    public async Task GetAuthenticationStateAsync_ReturnsUnauthenticated_When_NetworkFails()
    {
        var factory  = BuildFactory(new ThrowingHandler());
        var provider = new AppSessionAuthStateProvider(factory);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    // ── Fake handlers ─────────────────────────────────────────────────────────

    private sealed class StaticResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class JsonResponseHandler(MeResponse payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network error");
    }
}
