using HR.Web.Services;
using Microsoft.AspNetCore.Http;

namespace HR.Web.Tests;

// Regression tests for the P1 captive-dependency token leak, and its Ticket 9 follow-up: the token
// is now delivered via CircuitSessionState, a genuine per-circuit Scoped DI object, not an
// AsyncLocal ambient value. These tests exercise SupabaseSessionAccessor + CircuitSessionState
// together to prove no caller can observe another caller's token, and that logout/clear behaviour
// fails closed.
public class SupabaseSessionAccessorTests
{
    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static HttpContext BuildHttpContextWithCookie(string cookieName, string? cookieValue)
    {
        var context = new DefaultHttpContext();
        if (cookieValue is not null)
        {
            context.Request.Headers.Append("Cookie", $"{cookieName}={cookieValue}");
        }
        return context;
    }

    [Fact]
    public void AccessToken_FailsClosed_When_LiveHttpContext_Has_No_Cookie_Even_After_Prior_Token()
    {
        var accessor = new FakeHttpContextAccessor();
        var sessionState = new CircuitSessionState();
        var sut = new SupabaseSessionAccessor(accessor, sessionState);

        // Prior call on the same instance captured a real token for a different HttpContext.
        accessor.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-a");
        Assert.Equal("token-a", sut.AccessToken);

        // A new, unrelated live HttpContext with no cookie must NOT fall back to the stale value.
        accessor.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, null);
        Assert.Null(sut.AccessToken);
    }

    [Fact]
    public void AccessToken_AlwaysReflects_The_Live_Cookie_On_Sequential_Requests()
    {
        var accessor = new FakeHttpContextAccessor();
        var sut = new SupabaseSessionAccessor(accessor, new CircuitSessionState());

        accessor.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-a");
        Assert.Equal("token-a", sut.AccessToken);

        accessor.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-b");
        Assert.Equal("token-b", sut.AccessToken);
    }

    [Fact]
    public void AccessToken_Does_Not_Leak_Between_Separate_CircuitSessionState_Instances()
    {
        // Two separate CircuitSessionState instances stand in for two separate Blazor Server
        // circuits/DI scopes. Unlike the old AsyncLocal design, isolation here does not depend on
        // ExecutionContext flow at all — it is enforced by each circuit getting its own scoped
        // object, exactly like every other per-circuit service in this app.
        var accessorA = new FakeHttpContextAccessor();
        var stateA = new CircuitSessionState();
        var sutA = new SupabaseSessionAccessor(accessorA, stateA);

        accessorA.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-a");
        Assert.Equal("token-a", sutA.AccessToken);

        // A brand-new circuit's accessor/state pair, with no live HttpContext at all (simulating an
        // interactive event handler on a fresh, unauthenticated circuit) — must fail closed, never
        // inherit "token-a".
        var accessorB = new FakeHttpContextAccessor { HttpContext = null };
        var stateB = new CircuitSessionState();
        var sutB = new SupabaseSessionAccessor(accessorB, stateB);

        Assert.Null(sutB.AccessToken);

        // The original circuit must still see its own token, proving isolation is mutual.
        Assert.Equal("token-a", sutA.AccessToken);
    }

    [Fact]
    public void AccessToken_Retains_Own_Captured_Token_When_HttpContext_Later_Disappears()
    {
        var accessor = new FakeHttpContextAccessor();
        var sessionState = new CircuitSessionState();
        var sut = new SupabaseSessionAccessor(accessor, sessionState);

        // Initial pre-render request captures the real token via the live cookie.
        accessor.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-a");
        Assert.Equal("token-a", sut.AccessToken);

        // Later interactive circuit event handling has no HttpContext at all. Because
        // CircuitSessionState is a real, independently-held object (not an AsyncLocal value that
        // depends on ExecutionContext flow), this works even when the read happens on a completely
        // different thread/logical call chain than the one that captured the token — unlike the old
        // AsyncLocal-based design, which only worked by coincidence when the test itself never left
        // the original logical chain.
        accessor.HttpContext = null;
        Assert.Equal("token-a", sut.AccessToken);
    }

    [Fact]
    public async Task AccessToken_Retains_Own_Token_Across_A_Real_ExecutionContext_Boundary()
    {
        // This is the scenario the old AsyncLocal design actually got wrong: a later "circuit event"
        // that begins its own fresh logical call chain (simulated here via ExecutionContext.SuppressFlow,
        // just like Blazor Server's SignalR message dispatch does not nest inside the ExecutionContext
        // of the request that established the circuit). CircuitSessionState must still resolve
        // correctly because it is a plain object reference held by the test, not something that
        // needs to flow through ExecutionContext at all.
        var accessor = new FakeHttpContextAccessor();
        var sessionState = new CircuitSessionState();
        var sut = new SupabaseSessionAccessor(accessor, sessionState);

        accessor.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-a");
        Assert.Equal("token-a", sut.AccessToken);

        // No live HttpContext on this "later circuit event".
        accessor.HttpContext = null;

        Task<string?> task;
        var flowControl = ExecutionContext.SuppressFlow();
        try
        {
            task = Task.Factory.StartNew(() => sut.AccessToken, TaskCreationOptions.LongRunning);
        }
        finally
        {
            flowControl.Undo();
        }

        Assert.Equal("token-a", await task);
    }

    [Fact]
    public void ClearSessionCookie_Clears_CircuitSessionState_So_Later_Reads_Fail_Closed()
    {
        var accessor = new FakeHttpContextAccessor();
        var sessionState = new CircuitSessionState();
        var sut = new SupabaseSessionAccessor(accessor, sessionState);

        accessor.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-a");
        Assert.Equal("token-a", sut.AccessToken);

        var logoutContext = new DefaultHttpContext();
        SupabaseSessionAccessor.ClearSessionCookie(logoutContext, new HostEnvironmentStub(), sessionState);

        Assert.Null(sessionState.AccessToken);

        // A later read with no live HttpContext at all (e.g. a racing background continuation) must
        // not resurrect the pre-logout token.
        accessor.HttpContext = null;
        Assert.Null(sut.AccessToken);
    }

    private sealed class HostEnvironmentStub : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
