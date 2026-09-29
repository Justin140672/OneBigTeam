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
        var accessorA = new FakeHttpContextAccessor();
        var stateA = new CircuitSessionState();
        var sutA = new SupabaseSessionAccessor(accessorA, stateA);

        accessorA.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-a");
        Assert.Equal("token-a", sutA.AccessToken);

        var accessorB = new FakeHttpContextAccessor { HttpContext = null };
        var stateB = new CircuitSessionState();
        var sutB = new SupabaseSessionAccessor(accessorB, stateB);

        Assert.Null(sutB.AccessToken);

        Assert.Equal("token-a", sutA.AccessToken);
    }

    [Fact]
    public void AccessToken_Retains_Own_Captured_Token_When_HttpContext_Later_Disappears()
    {
        var accessor = new FakeHttpContextAccessor();
        var sessionState = new CircuitSessionState();
        var sut = new SupabaseSessionAccessor(accessor, sessionState);

        accessor.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-a");
        Assert.Equal("token-a", sut.AccessToken);

        accessor.HttpContext = null;
        Assert.Equal("token-a", sut.AccessToken);
    }

    [Fact]
    public async Task AccessToken_Retains_Own_Token_Across_A_Real_ExecutionContext_Boundary()
    {
        var accessor = new FakeHttpContextAccessor();
        var sessionState = new CircuitSessionState();
        var sut = new SupabaseSessionAccessor(accessor, sessionState);

        accessor.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-a");
        Assert.Equal("token-a", sut.AccessToken);

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
