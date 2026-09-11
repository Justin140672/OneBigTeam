using HR.Admin.Web.Services;
using Microsoft.AspNetCore.Http;

namespace HR.Admin.Web.Tests;

// Regression tests for the P1 captive-dependency token leak, and its Ticket 9 follow-up: the token
// is now delivered via CircuitSessionState, a genuine per-circuit Scoped DI object, not an
// AsyncLocal ambient value. See HR.Web.Tests.SupabaseSessionAccessorTests for the identical
// rationale (this app mirrors HR.Web's design exactly).
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
        var sut = new SupabaseSessionAccessor(accessor, new CircuitSessionState());

        accessor.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-a");
        Assert.Equal("token-a", sut.AccessToken);

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
        // Two separate CircuitSessionState instances stand in for two separate admin-portal
        // circuits/DI scopes (e.g. two different platform administrators, or the same administrator
        // in two tabs). Isolation does not depend on ExecutionContext flow at all.
        var accessorA = new FakeHttpContextAccessor();
        var sutA = new SupabaseSessionAccessor(accessorA, new CircuitSessionState());

        accessorA.HttpContext = BuildHttpContextWithCookie(SupabaseSessionAccessor.CookieName, "token-a");
        Assert.Equal("token-a", sutA.AccessToken);

        // A brand-new circuit's accessor/state pair — the previous (buggy) implementation latched
        // onto the FIRST captured token forever and would have returned "token-a" here too.
        var accessorB = new FakeHttpContextAccessor { HttpContext = null };
        var sutB = new SupabaseSessionAccessor(accessorB, new CircuitSessionState());

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
