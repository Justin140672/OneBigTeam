using System.Net;
using System.Net.Http.Headers;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

/// <summary>
/// Cross-tab session revocation on a LIVE circuit: when HR.Api rejects the circuit's current bearer
/// token (JwtBearer challenge "Bearer error=\"invalid_token\"" — e.g. the session was revoked by a
/// logout in another tab), HrApiHttpClientFactory's client reports it to CircuitSessionState, which
/// fails closed and raises SessionRejectedByServer (AppSessionAuthStateProvider turns that into an
/// anonymous state; AppSession then force-navigates to /login). An endpoint's own 401 without that
/// challenge must NOT sign the circuit out.
/// </summary>
public class SessionRevocationDetectionTests
{
    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }

    private static (HrApiHttpClientFactory Factory, CircuitSessionState State) Build(Func<HttpResponseMessage> respond)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(respond));
        services.AddScoped<CircuitSessionState>();
        services.AddScoped<HrApiHttpClientFactory>();

        var scope = services.BuildServiceProvider().CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<CircuitSessionState>();
        state.SetToken("live-token");
        return (scope.ServiceProvider.GetRequiredService<HrApiHttpClientFactory>(), state);
    }

    private static HttpResponseMessage Unauthorized(string? challengeParameter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        if (challengeParameter is not null)
            response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", challengeParameter));
        return response;
    }

    [Fact]
    public async Task InvalidTokenChallenge_InvalidatesCircuit_AndRaisesRejection()
    {
        var (factory, state) = Build(() => Unauthorized("error=\"invalid_token\", error_description=\"Session has been revoked.\""));
        var raised = 0;
        state.SessionRejectedByServer += () => raised++;

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("api/employees");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, raised);
        Assert.Equal(CircuitAuthStatus.Invalidated, state.Status);
        Assert.Null(state.AccessToken);
    }

    [Fact]
    public async Task EndpointOwn401_WithoutInvalidTokenChallenge_DoesNotSignOut()
    {
        var (factory, state) = Build(() => Unauthorized(null));
        var raised = 0;
        state.SessionRejectedByServer += () => raised++;

        using var client = factory.CreateClient();
        using var _ = await client.GetAsync("api/something");

        Assert.Equal(0, raised);
        Assert.Equal(CircuitAuthStatus.Authenticated, state.Status);
        Assert.Equal("live-token", state.AccessToken);
    }

    [Fact]
    public void ReportTokenRejected_ForAStaleToken_IsIgnored()
    {
        var state = new CircuitSessionState();
        state.SetToken("new-token");
        var raised = 0;
        state.SessionRejectedByServer += () => raised++;

        state.ReportTokenRejected("old-token");

        Assert.Equal(0, raised);
        Assert.Equal(CircuitAuthStatus.Authenticated, state.Status);
    }

    [Fact]
    public void ReportTokenRejected_RaisesOnlyOnce()
    {
        var state = new CircuitSessionState();
        state.SetToken("t");
        var raised = 0;
        state.SessionRejectedByServer += () => raised++;

        state.ReportTokenRejected("t");
        state.ReportTokenRejected("t");

        Assert.Equal(1, raised);
    }
}
