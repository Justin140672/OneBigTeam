using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// Covers the P1 "Add abuse protection to public identity endpoints" rate limiters
/// (src/HR.Api/RateLimiting/IdentityRateLimiting.cs) against POST /api/forgot-password, chosen
/// because RequestPasswordResetHandler always returns 200 without calling any external gateway when
/// the supplied email has no matching profile — the cheapest of the six identity endpoints to call
/// repeatedly with no test-double arrangement required.
///
/// Runs against the dedicated <see cref="IdentityRateLimitApiWebApplicationFactory"/> (PermitLimit=3,
/// WindowSeconds=3 for identity-forgot-password only), NOT the shared <see cref="ApiWebApplicationFactory"/>
/// whose identity limits are deliberately widened to 1000 so the rest of the suite never trips them.
///
/// Every test uses its own unique email and/or X-Test-Remote-Ip value so the fixed-window limiter's
/// in-memory partition state (shared across all test methods via IClassFixture) never leaks between
/// test methods.
/// </summary>
// Part of the "Integration" collection so ApiWebApplicationFactory's shared Postgres container is
// started (and its connection string exported) before this factory builds its host — mirrors
// ContactApiWebApplicationFactory's identical convention.
[Collection("Integration")]
public sealed class IdentityRateLimitingTests : IClassFixture<IdentityRateLimitApiWebApplicationFactory>
{
    private readonly IdentityRateLimitApiWebApplicationFactory _factory;

    public IdentityRateLimitingTests(IdentityRateLimitApiWebApplicationFactory factory, ApiWebApplicationFactory _)
    {
        _factory = factory;
    }

    private static string UniqueEmail([System.Runtime.CompilerServices.CallerMemberName] string? testName = null) =>
        $"{testName}-{Guid.NewGuid():N}@example.com";

    private HttpClient CreateClientWithIp(string remoteIp)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.HeaderName, remoteIp);
        return client;
    }

    private static Task<HttpResponseMessage> PostForgotPasswordAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/forgot-password", new { email });

    [Fact]
    public async Task Exceeding_PermitLimit_Returns_429_With_Contract_And_RetryAfter_While_Requests_Within_Limit_Succeed()
    {
        using var client = CreateClientWithIp("10.1.1.1");
        var email = UniqueEmail();

        for (var i = 0; i < IdentityRateLimitApiWebApplicationFactory.PermitLimit; i++)
        {
            var response = await PostForgotPasswordAsync(client, email);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var rejected = await PostForgotPasswordAsync(client, email);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));

        var body = await rejected.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("Too many requests. Please try again later.", document.RootElement.GetProperty("error").GetString());
        Assert.Equal("rate_limited", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Requests_Succeed_Again_After_The_Window_Elapses()
    {
        using var client = CreateClientWithIp("10.1.1.2");
        var email = UniqueEmail();

        for (var i = 0; i < IdentityRateLimitApiWebApplicationFactory.PermitLimit; i++)
        {
            var response = await PostForgotPasswordAsync(client, email);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var rejected = await PostForgotPasswordAsync(client, email);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        // Real-time wait past the short test-only window (WindowSeconds=3) — this test is
        // deliberately slower than the rest of the suite. A fake-TimeProvider-based approach was
        // considered (FixedWindowRateLimiterOptions has no public TimeProvider hook reachable
        // through RateLimiterOptions.AddPolicy's factory delegate in the installed
        // System.Threading.RateLimiting version), so a short real window plus a short real sleep is
        // used instead — see IdentityRateLimitApiWebApplicationFactory's remarks.
        await Task.Delay(TimeSpan.FromSeconds(IdentityRateLimitApiWebApplicationFactory.WindowSeconds + 1));

        var afterWindow = await PostForgotPasswordAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, afterWindow.StatusCode);
    }

    [Fact]
    public async Task Different_Client_Ips_Get_Independent_Quotas_For_The_Same_Email()
    {
        var email = UniqueEmail();
        using var clientA = CreateClientWithIp("10.2.2.1");
        using var clientB = CreateClientWithIp("10.2.2.2");

        for (var i = 0; i < IdentityRateLimitApiWebApplicationFactory.PermitLimit; i++)
        {
            var response = await PostForgotPasswordAsync(clientA, email);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var rejectedOnA = await PostForgotPasswordAsync(clientA, email);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedOnA.StatusCode);

        var firstOnB = await PostForgotPasswordAsync(clientB, email);
        Assert.Equal(HttpStatusCode.OK, firstOnB.StatusCode);
    }

    [Fact]
    public async Task Different_Emails_Get_Independent_Quotas_For_The_Same_Ip()
    {
        using var client = CreateClientWithIp("10.3.3.1");
        var emailA = UniqueEmail() + "-a";
        var emailB = UniqueEmail() + "-b";

        for (var i = 0; i < IdentityRateLimitApiWebApplicationFactory.PermitLimit; i++)
        {
            var response = await PostForgotPasswordAsync(client, emailA);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var rejectedOnA = await PostForgotPasswordAsync(client, emailA);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedOnA.StatusCode);

        var firstOnB = await PostForgotPasswordAsync(client, emailB);
        Assert.Equal(HttpStatusCode.OK, firstOnB.StatusCode);
    }

    [Fact]
    public async Task Untrusted_Callers_XForwardedFor_Header_Is_Ignored_For_Partitioning()
    {
        // Identity:TrustedProxies/TrustedProxyNetworks are NOT configured in
        // IdentityRateLimitApiWebApplicationFactory, so ForwardedHeadersOptions never honours
        // X-Forwarded-For for a connection whose real (test-simulated) IP isn't a configured trusted
        // proxy. The real connection IP stays fixed across all requests below; only the
        // (ineffective) X-Forwarded-For header value changes per request. If the header were
        // honoured, each request would land in its own untouched partition and none would ever be
        // rejected — proving the opposite (all requests share ONE partition and the limit still
        // trips) demonstrates the spoofing attempt failed.
        const string realIp = "10.4.4.1";
        var email = UniqueEmail();

        for (var i = 0; i < IdentityRateLimitApiWebApplicationFactory.PermitLimit; i++)
        {
            using var client = CreateClientWithIp(realIp);
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"203.0.113.{i}");

            var response = await PostForgotPasswordAsync(client, email);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var finalClient = CreateClientWithIp(realIp);
        finalClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.250");

        var rejected = await PostForgotPasswordAsync(finalClient, email);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task Concurrent_Requests_Never_Allow_More_Than_PermitLimit_Successes()
    {
        using var client = CreateClientWithIp("10.5.5.1");
        var email = UniqueEmail();

        var tasks = Enumerable.Range(0, IdentityRateLimitApiWebApplicationFactory.PermitLimit + 5)
            .Select(_ => PostForgotPasswordAsync(client, email))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var rejectedCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);

        Assert.True(
            successCount <= IdentityRateLimitApiWebApplicationFactory.PermitLimit,
            $"Expected at most {IdentityRateLimitApiWebApplicationFactory.PermitLimit} successes under concurrency, got {successCount}.");
        Assert.Equal(responses.Length, successCount + rejectedCount);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
