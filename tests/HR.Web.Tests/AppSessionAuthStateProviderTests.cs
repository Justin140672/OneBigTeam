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

    private static HrApiHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return new HrApiHttpClientFactory(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), new CircuitSessionState());
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuthenticationStateAsync_ReturnsUnauthenticated_When_ApiMe_Returns401()
    {
        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.Unauthorized));
        var provider = new AppSessionAuthStateProvider(factory, new CircuitSessionState());

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task GetAuthenticationStateAsync_ReturnsUnauthenticated_When_ApiMe_Returns403()
    {
        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.Forbidden));
        var provider = new AppSessionAuthStateProvider(factory, new CircuitSessionState());

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
        var provider = new AppSessionAuthStateProvider(factory, new CircuitSessionState());

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
        var provider = new AppSessionAuthStateProvider(factory, new CircuitSessionState());

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    // ── Ticket: circuit-scope token bridging regression tests ──────────────────────────────────
    // These exercise the PRODUCTION SetAuthenticationState path (IHostEnvironmentAuthenticationStateProvider),
    // which is what Blazor Server's CircuitHost actually calls at circuit creation — NOT manual
    // CircuitSessionState.SetToken seeding, which would prove nothing about whether the bridge itself
    // works. See NoOpAuthenticationHandler.SupabaseAccessTokenClaimType and
    // AppSessionAuthStateProvider.SetAuthenticationState for the production mechanism being tested.

    private static ClaimsPrincipal BuildPrincipalWithAccessTokenClaim(string token) =>
        new(new ClaimsIdentity(
            [new Claim(NoOpAuthenticationHandler.SupabaseAccessTokenClaimType, token)],
            authenticationType: "TestScheme"));

    [Fact]
    public void SetAuthenticationState_SeedsOnlyTheCircuitsOwnSessionState_NotTheRequestScopesInstance()
    {
        // Simulates two separate DI scopes: "requestScopeSessionState" stands in for the instance
        // Program.cs's request middleware resolved and populated from the negotiating HTTP request's
        // own scope (the one that never reaches the circuit in the original bug); "circuitSessionState"
        // stands in for the circuit's own, otherwise-uninitiated CircuitSessionState instance.
        var requestScopeSessionState = new CircuitSessionState();
        var circuitSessionState      = new CircuitSessionState();

        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.OK));
        var provider = new AppSessionAuthStateProvider(factory, circuitSessionState);

        const string token = "circuit-only-token";
        var principal = BuildPrincipalWithAccessTokenClaim(token);

        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(principal)));

        Assert.Equal(token, circuitSessionState.AccessToken);
        Assert.Null(requestScopeSessionState.AccessToken);

        // Proves the seeded token actually reaches an outgoing request via the same production
        // component (HrApiHttpClientFactory) that every real circuit call goes through.
        var httpFactory = new HrApiHttpClientFactory(
            BuildHttpClientFactory(new StaticResponseHandler(HttpStatusCode.OK)), circuitSessionState);
        using var client = httpFactory.CreateClient();

        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal(token, client.DefaultRequestHeaders.Authorization?.Parameter);
    }

    [Fact]
    public void SetAuthenticationState_AnonymousPrincipal_LeavesSessionStateNull_AndSendsNoAuthorizationHeader()
    {
        var circuitSessionState = new CircuitSessionState();
        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.OK));
        var provider = new AppSessionAuthStateProvider(factory, circuitSessionState);

        var anonymousPrincipal = new ClaimsPrincipal(new ClaimsIdentity());

        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(anonymousPrincipal)));

        Assert.Null(circuitSessionState.AccessToken);

        var httpFactory = new HrApiHttpClientFactory(
            BuildHttpClientFactory(new StaticResponseHandler(HttpStatusCode.OK)), circuitSessionState);
        using var client = httpFactory.CreateClient();

        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task SetAuthenticationState_TwoSimultaneousCircuits_NeverCrossContaminateTokens()
    {
        var sessionStateA = new CircuitSessionState();
        var sessionStateB = new CircuitSessionState();

        var providerA = new AppSessionAuthStateProvider(
            BuildFactory(new StaticResponseHandler(HttpStatusCode.OK)), sessionStateA);
        var providerB = new AppSessionAuthStateProvider(
            BuildFactory(new StaticResponseHandler(HttpStatusCode.OK)), sessionStateB);

        var principalA = BuildPrincipalWithAccessTokenClaim("token-circuit-a");
        var principalB = BuildPrincipalWithAccessTokenClaim("token-circuit-b");

        await Task.WhenAll(
            Task.Run(() => ((IHostEnvironmentAuthenticationStateProvider)providerA)
                .SetAuthenticationState(Task.FromResult(new AuthenticationState(principalA)))),
            Task.Run(() => ((IHostEnvironmentAuthenticationStateProvider)providerB)
                .SetAuthenticationState(Task.FromResult(new AuthenticationState(principalB)))));

        Assert.Equal("token-circuit-a", sessionStateA.AccessToken);
        Assert.Equal("token-circuit-b", sessionStateB.AccessToken);
    }

    private static IHttpClientFactory BuildHttpClientFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    // ── Ticket 11: reconnect must be treated as authoritative, not just an opportunistic seed ──
    // SetAuthenticationState is called by Blazor Server's CircuitHost not only at circuit creation
    // but also on every RECONNECT of an existing circuit (see
    // https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Components/Server/src/ComponentHub.cs#L211).
    // These tests start with an ALREADY-POPULATED CircuitSessionState (simulating the circuit's
    // initial authenticated seed) and then invoke SetAuthenticationState again, simulating a
    // reconnect, to prove no stale token from the original circuit-creation seed survives.

    [Fact]
    public void SetAuthenticationState_ReconnectAnonymous_ClearsPreviouslyPopulatedSessionState()
    {
        var circuitSessionState = new CircuitSessionState();
        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.OK));
        var provider = new AppSessionAuthStateProvider(factory, circuitSessionState);

        // Simulate the circuit's initial authenticated seed (circuit creation with a valid cookie).
        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-a"))));
        Assert.Equal("token-a", circuitSessionState.AccessToken);

        // Simulate a RECONNECT of the same live circuit without the session cookie (e.g. logout or
        // cookie expiry in between) — NoOpAuthenticationHandler now authenticates anonymously.
        var anonymousPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(anonymousPrincipal)));

        Assert.Null(circuitSessionState.AccessToken);

        var httpFactory = new HrApiHttpClientFactory(
            BuildHttpClientFactory(new StaticResponseHandler(HttpStatusCode.OK)), circuitSessionState);
        using var client = httpFactory.CreateClient();

        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void SetAuthenticationState_ReconnectAuthenticatedButMissingTokenClaim_ClearsPreviouslyPopulatedSessionState()
    {
        var circuitSessionState = new CircuitSessionState();
        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.OK));
        var provider = new AppSessionAuthStateProvider(factory, circuitSessionState);

        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-a"))));
        Assert.Equal("token-a", circuitSessionState.AccessToken);

        // Authenticated identity (e.g. some other scheme) but without the Supabase token claim —
        // an edge case distinct from fully anonymous; must still fail closed.
        var principalWithoutTokenClaim = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "someone")], authenticationType: "TestScheme"));
        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(principalWithoutTokenClaim)));

        Assert.Null(circuitSessionState.AccessToken);

        var httpFactory = new HrApiHttpClientFactory(
            BuildHttpClientFactory(new StaticResponseHandler(HttpStatusCode.OK)), circuitSessionState);
        using var client = httpFactory.CreateClient();

        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void SetAuthenticationState_ReconnectDifferentUser_FailsClosed_DoesNotHotSwapToNewToken()
    {
        var circuitSessionState = new CircuitSessionState();
        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.OK));
        var provider = new AppSessionAuthStateProvider(factory, circuitSessionState);

        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-user-a"))));
        Assert.Equal("token-user-a", circuitSessionState.AccessToken);

        // Reconnect with a DIFFERENT authenticated identity's token — per the Ticket 11 policy
        // decision, a live circuit must never hot-swap to a different user's identity/token; it
        // fails closed instead so the browser is forced through a fresh circuit/full navigation.
        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-user-b"))));

        Assert.Null(circuitSessionState.AccessToken);
        Assert.NotEqual("token-user-b", circuitSessionState.AccessToken);

        var httpFactory = new HrApiHttpClientFactory(
            BuildHttpClientFactory(new StaticResponseHandler(HttpStatusCode.OK)), circuitSessionState);
        using var client = httpFactory.CreateClient();

        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void SetAuthenticationState_ReconnectSameUserSameToken_PreservesValidAuthentication()
    {
        var circuitSessionState = new CircuitSessionState();
        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.OK));
        var provider = new AppSessionAuthStateProvider(factory, circuitSessionState);

        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-a"))));
        Assert.Equal("token-a", circuitSessionState.AccessToken);

        // Reconnect with the SAME still-valid session cookie/token — must be a no-op that preserves
        // the existing valid authentication rather than accidentally clearing it.
        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-a"))));

        Assert.Equal("token-a", circuitSessionState.AccessToken);
        // Ticket 12: the Status-based lifecycle must also remain Authenticated (not e.g. drift to
        // Invalidated) across a same-token reconnect no-op.
        Assert.Equal(CircuitAuthStatus.Authenticated, circuitSessionState.Status);

        var httpFactory = new HrApiHttpClientFactory(
            BuildHttpClientFactory(new StaticResponseHandler(HttpStatusCode.OK)), circuitSessionState);
        using var client = httpFactory.CreateClient();

        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal("token-a", client.DefaultRequestHeaders.Authorization?.Parameter);
    }

    // ── Ticket 12: Status-based lifecycle must stay sticky-invalidated, not just AccessToken-null ──
    // Before Ticket 12, "isFirstSeed" was inferred from AccessToken is null, which is ALSO true
    // immediately after Clear() has invalidated an already-used circuit — so the very next
    // SetAuthenticationState call on that SAME invalidated circuit was wrongly treated as a
    // brand-new, never-seeded circuit and happily accepted a new (possibly different-user) token.
    // These tests drive a THIRD SetAuthenticationState call (the one the old bug mishandled) and
    // assert it is still rejected, distinguishing the fix from the single-reconnect coverage above.

    [Fact]
    public void SetAuthenticationState_ReconnectSameUserThenDifferentUser_SecondDifferentUserCallIsRejected()
    {
        var circuitSessionState = new CircuitSessionState();
        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.OK));
        var provider = new AppSessionAuthStateProvider(factory, circuitSessionState);

        // A: initial authenticated seed.
        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-a"))));
        Assert.Equal("token-a", circuitSessionState.AccessToken);
        Assert.Equal(CircuitAuthStatus.Authenticated, circuitSessionState.Status);

        // B: a different identity arrives on the live circuit — fails closed, invalidates.
        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-b"))));
        Assert.Null(circuitSessionState.AccessToken);
        Assert.Equal(CircuitAuthStatus.Invalidated, circuitSessionState.Status);

        // B again: this is the call the old (pre-Ticket-12) AccessToken-is-null inference would have
        // wrongly accepted as "first seed" — it must still be rejected, and Status must stay
        // Invalidated (sticky), not flip back to Authenticated.
        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-b"))));
        Assert.Null(circuitSessionState.AccessToken);
        Assert.Equal(CircuitAuthStatus.Invalidated, circuitSessionState.Status);

        var httpFactory = new HrApiHttpClientFactory(
            BuildHttpClientFactory(new StaticResponseHandler(HttpStatusCode.OK)), circuitSessionState);
        using var client = httpFactory.CreateClient();

        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void SetAuthenticationState_ReconnectAnonymousThenDifferentUser_IsRejected()
    {
        var circuitSessionState = new CircuitSessionState();
        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.OK));
        var provider = new AppSessionAuthStateProvider(factory, circuitSessionState);

        // A: initial authenticated seed.
        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-a"))));
        Assert.Equal("token-a", circuitSessionState.AccessToken);

        // Anonymous reconnect — clears/invalidates.
        var anonymousPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(anonymousPrincipal)));
        Assert.Null(circuitSessionState.AccessToken);
        Assert.Equal(CircuitAuthStatus.Invalidated, circuitSessionState.Status);

        // A DIFFERENT token then arrives — this is the A -> anonymous -> B sequence the old bug also
        // mishandled (AccessToken was null after Clear(), so this looked like a fresh circuit).
        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-b"))));
        Assert.Null(circuitSessionState.AccessToken);
        Assert.Equal(CircuitAuthStatus.Invalidated, circuitSessionState.Status);

        var httpFactory = new HrApiHttpClientFactory(
            BuildHttpClientFactory(new StaticResponseHandler(HttpStatusCode.OK)), circuitSessionState);
        using var client = httpFactory.CreateClient();

        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void SetAuthenticationState_BrandNewCircuit_AcceptsFirstToken()
    {
        // A freshly constructed CircuitSessionState (Status defaults to Uninitialized) must still
        // accept its very first token normally — the negated branch of the Status check above.
        var circuitSessionState = new CircuitSessionState();
        Assert.Equal(CircuitAuthStatus.Uninitialized, circuitSessionState.Status);

        var factory  = BuildFactory(new StaticResponseHandler(HttpStatusCode.OK));
        var provider = new AppSessionAuthStateProvider(factory, circuitSessionState);

        ((IHostEnvironmentAuthenticationStateProvider)provider)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(BuildPrincipalWithAccessTokenClaim("token-a"))));

        Assert.Equal("token-a", circuitSessionState.AccessToken);
        Assert.Equal(CircuitAuthStatus.Authenticated, circuitSessionState.Status);
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
