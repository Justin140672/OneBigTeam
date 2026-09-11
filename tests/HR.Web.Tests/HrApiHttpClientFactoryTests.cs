using System.Collections.Concurrent;
using System.Net;
using HR.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

// Higher-level regression tests for Ticket 9 (the P1 captive-dependency token leak) that exercise
// real DI scopes and a real IHttpClientFactory-backed "hrapi" named HttpClient, rather than the
// plain-object tests in SupabaseSessionAccessorTests. These specifically prove that:
//   1. HrApiHttpClientFactory attaches the token from the CALLER'S OWN real DI scope, never from
//      IHttpClientFactory's internal HandlerLifetime-scoped container (the old captive-dependency
//      bug's root cause) — verified here by resolving HrApiHttpClientFactory from several distinct
//      IServiceScopes built on the SAME root ServiceProvider (so they all share the same
//      IHttpClientFactory / handler pool) and confirming each still sends only its own token.
//   2. This isolation holds under concurrent/interleaved use (many scopes hammering CreateClient()
//      at once), and across an ExecutionContext boundary (the closest local substitute for a fresh
//      Blazor Server circuit event dispatch without a real SignalR/browser harness).
//   3. Logout (ClearSessionCookie) fails closed for later calls on the same scope, even with no live
//      HttpContext.
public class HrApiHttpClientFactoryTests
{
    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class HostEnvironmentStub : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    // Records the Authorization header (or null) seen on every request it handles, keyed by an
    // opaque "caller id" the test stamps onto the request via a custom header, so assertions can
    // check "this caller's requests only ever carried this caller's token" even under concurrency.
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public ConcurrentBag<(string CallerId, string? AuthorizationHeader)> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var callerId = request.Headers.TryGetValues("X-Test-Caller-Id", out var values) ? values.First() : "unknown";
            var auth = request.Headers.Authorization?.ToString();
            Requests.Add((callerId, auth));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    private static HttpContext BuildHttpContextWithCookie(string? cookieValue)
    {
        var context = new DefaultHttpContext();
        if (cookieValue is not null)
        {
            context.Request.Headers.Append("Cookie", $"{SupabaseSessionAccessor.CookieName}={cookieValue}");
        }
        return context;
    }

    private static (ServiceProvider Root, CapturingHandler Handler) BuildRootProvider()
    {
        var handler = new CapturingHandler();
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddScoped<CircuitSessionState>();
        services.AddScoped<HrApiHttpClientFactory>();
        services.AddScoped<SupabaseSessionAccessor>();
        return (services.BuildServiceProvider(), handler);
    }

    private static async Task<string?> SendTaggedRequestAsync(HrApiHttpClientFactory factory, string callerId)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "ping");
        request.Headers.Add("X-Test-Caller-Id", callerId);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return client.DefaultRequestHeaders.Authorization?.ToString();
    }

    // ── Scenario A: concurrent live requests across separate real IServiceScopes ────────────────

    [Fact]
    public async Task CreateClient_Attaches_Only_Its_Own_Scopes_Token_Across_Concurrent_Real_Scopes()
    {
        var (root, handler) = BuildRootProvider();
        using var rootDisposable = root;

        for (var iteration = 0; iteration < 25; iteration++)
        {
            using var scopeA = root.CreateScope();
            using var scopeB = root.CreateScope();
            using var scopeAnonymous = root.CreateScope();

            scopeA.ServiceProvider.GetRequiredService<CircuitSessionState>().SetToken("token-a");
            scopeB.ServiceProvider.GetRequiredService<CircuitSessionState>().SetToken("token-b");
            // scopeAnonymous: no token set at all — simulates an unauthenticated third circuit.

            var factoryA = scopeA.ServiceProvider.GetRequiredService<HrApiHttpClientFactory>();
            var factoryB = scopeB.ServiceProvider.GetRequiredService<HrApiHttpClientFactory>();
            var factoryAnon = scopeAnonymous.ServiceProvider.GetRequiredService<HrApiHttpClientFactory>();

            var callerA = $"a-{iteration}";
            var callerB = $"b-{iteration}";
            var callerAnon = $"anon-{iteration}";

            var results = await Task.WhenAll(
                SendTaggedRequestAsync(factoryA, callerA),
                SendTaggedRequestAsync(factoryB, callerB),
                SendTaggedRequestAsync(factoryAnon, callerAnon));

            Assert.Equal("Bearer token-a", results[0]);
            Assert.Equal("Bearer token-b", results[1]);
            Assert.Null(results[2]);
        }

        // Cross-check every captured request against the handler's own record: no caller ever saw
        // a token that wasn't theirs.
        foreach (var (callerId, authorizationHeader) in handler.Requests)
        {
            if (callerId.StartsWith("a-", StringComparison.Ordinal))
            {
                Assert.Equal("Bearer token-a", authorizationHeader);
            }
            else if (callerId.StartsWith("b-", StringComparison.Ordinal))
            {
                Assert.Equal("Bearer token-b", authorizationHeader);
            }
            else if (callerId.StartsWith("anon-", StringComparison.Ordinal))
            {
                Assert.Null(authorizationHeader);
            }
        }
    }

    // ── Scenario B: captured via live HttpContext, then resolved again later in the SAME scope ──
    // ── with no live HttpContext, across a real ExecutionContext boundary ────────────────────────

    [Fact]
    public async Task CreateClient_Later_In_Same_Scope_Still_Attaches_Captured_Token_Across_ExecutionContext_Boundary()
    {
        var (root, handler) = BuildRootProvider();
        using var rootDisposable = root;
        using var scope = root.CreateScope();

        // The real IHttpContextAccessor from AddHttpContextAccessor() has no ambient context in this
        // unit test host, so drive SupabaseSessionAccessor directly with a fake instead, wired to the
        // SAME CircuitSessionState instance the scope will later resolve HrApiHttpClientFactory from
        // — exactly like the real DI graph (both are Scoped, from the same scope).
        var fakeAccessor = new FakeHttpContextAccessor { HttpContext = BuildHttpContextWithCookie("token-a") };
        var sessionState = scope.ServiceProvider.GetRequiredService<CircuitSessionState>();
        var sessionAccessor = new SupabaseSessionAccessor(fakeAccessor, sessionState);

        // First async invocation: reads the live cookie, which populates CircuitSessionState as a
        // side effect (mirroring the initial pre-render HTTP request in the real app).
        Assert.Equal("token-a", sessionAccessor.AccessToken);

        // Later interactive circuit event handling has no live HttpContext at all.
        fakeAccessor.HttpContext = null;

        // Simulate a genuinely separate top-level async operation on the SAME scope, deliberately not
        // nested in the ExecutionContext of the call above (mirrors Blazor Server's SignalR dispatch
        // of each interactive event as its own fresh logical call chain).
        Task<string?> task;
        var flowControl = ExecutionContext.SuppressFlow();
        try
        {
            task = Task.Factory.StartNew(
                async () =>
                {
                    var factory = scope.ServiceProvider.GetRequiredService<HrApiHttpClientFactory>();
                    return await SendTaggedRequestAsync(factory, "later-same-scope");
                },
                TaskCreationOptions.LongRunning).Unwrap();
        }
        finally
        {
            flowControl.Undo();
        }

        var authorizationHeader = await task;

        Assert.Equal("Bearer token-a", authorizationHeader);
        Assert.Contains(handler.Requests, r => r.CallerId == "later-same-scope" && r.AuthorizationHeader == "Bearer token-a");
    }

    // ── Scenario C: logout (ClearSessionCookie) fails closed for later calls on the same scope ───

    [Fact]
    public async Task CreateClient_Sends_No_Authorization_After_Logout_Clears_The_Same_Scopes_SessionState()
    {
        var (root, handler) = BuildRootProvider();
        using var rootDisposable = root;
        using var scope = root.CreateScope();

        var sessionState = scope.ServiceProvider.GetRequiredService<CircuitSessionState>();
        var fakeAccessor = new FakeHttpContextAccessor { HttpContext = BuildHttpContextWithCookie("token-a") };
        var sessionAccessor = new SupabaseSessionAccessor(fakeAccessor, sessionState);

        // Capture token-a for this scope, exactly like the initial authenticated request.
        Assert.Equal("token-a", sessionAccessor.AccessToken);

        var factory = scope.ServiceProvider.GetRequiredService<HrApiHttpClientFactory>();
        var beforeLogout = await SendTaggedRequestAsync(factory, "before-logout");
        Assert.Equal("Bearer token-a", beforeLogout);

        // Simulate the /logout minimal API endpoint: a real HttpContext for the logout request/response
        // itself, and the SAME scope's CircuitSessionState passed through so it is cleared together
        // with the cookie.
        var logoutContext = new DefaultHttpContext();
        SupabaseSessionAccessor.ClearSessionCookie(logoutContext, new HostEnvironmentStub(), sessionState);

        // Later call on the same scope, with no live HttpContext at all (a racing background
        // continuation) — must fail closed, never resume sending token-a.
        fakeAccessor.HttpContext = null;
        var afterLogout = await SendTaggedRequestAsync(factory, "after-logout");

        Assert.Null(afterLogout);
        Assert.Contains(handler.Requests, r => r.CallerId == "after-logout" && r.AuthorizationHeader == null);
    }

    // ── Concurrent two-circuit interleaving stress test ─────────────────────────────────────────

    [Fact]
    public async Task Two_Concurrent_Circuits_Never_Observe_Each_Others_Token_Across_Many_Interleavings()
    {
        var (root, handler) = BuildRootProvider();
        using var rootDisposable = root;

        using var scopeA = root.CreateScope();
        using var scopeB = root.CreateScope();

        scopeA.ServiceProvider.GetRequiredService<CircuitSessionState>().SetToken("token-a");
        scopeB.ServiceProvider.GetRequiredService<CircuitSessionState>().SetToken("token-b");

        var factoryA = scopeA.ServiceProvider.GetRequiredService<HrApiHttpClientFactory>();
        var factoryB = scopeB.ServiceProvider.GetRequiredService<HrApiHttpClientFactory>();

        const int interleavings = 75;
        var tasks = new List<Task<(string Owner, string? AuthorizationHeader)>>();

        for (var i = 0; i < interleavings; i++)
        {
            var indexA = i;
            var indexB = i;
            tasks.Add(Task.Run(async () =>
                ("A", await SendTaggedRequestAsync(factoryA, $"tab-a-{indexA}"))));
            tasks.Add(Task.Run(async () =>
                ("B", await SendTaggedRequestAsync(factoryB, $"tab-b-{indexB}"))));
        }

        var results = await Task.WhenAll(tasks);

        foreach (var (owner, authorizationHeader) in results)
        {
            var expected = owner == "A" ? "Bearer token-a" : "Bearer token-b";
            Assert.Equal(expected, authorizationHeader);
        }

        // Belt-and-braces: check the handler's own captured record too, keyed by tag prefix.
        foreach (var (callerId, authorizationHeader) in handler.Requests)
        {
            if (callerId.StartsWith("tab-a-", StringComparison.Ordinal))
            {
                Assert.Equal("Bearer token-a", authorizationHeader);
            }
            else if (callerId.StartsWith("tab-b-", StringComparison.Ordinal))
            {
                Assert.Equal("Bearer token-b", authorizationHeader);
            }
        }
    }
}
