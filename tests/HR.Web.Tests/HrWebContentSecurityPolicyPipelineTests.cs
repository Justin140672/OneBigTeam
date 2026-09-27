using System.Net;
using System.Text.RegularExpressions;
using HR.SharedKernel.Http;
using HR.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

/// <summary>
/// [P2] Content Security Policy — boots the real HR.Web pipeline (Program.cs) in-memory and asserts
/// that the header is present on every kind of response (Razor page, static asset, minimal-API page,
/// re-executed 404 and exception pages, redirects), that each response gets a fresh nonce, and that
/// the nonce is applied to every inline script HR.Web emits (App.razor and the auth hand-off pages).
/// </summary>
public sealed partial class HrWebContentSecurityPolicyPipelineTests : IDisposable
{
    private const string ProductionHost = "app.onebigteam.example";
    private const string SupabaseOrigin = "https://abcdefgh.supabase.co";
    private const string ThrowPath = "/__csp-test/throw";

    private readonly HrWebFactory _production = new("Production");
    private readonly HrWebFactory _development = new("Development");

    public void Dispose()
    {
        _production.Dispose();
        _development.Dispose();
    }

    private static HttpClient Client(HrWebFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri($"https://{ProductionHost}"),
        });

    private static (IReadOnlyDictionary<string, IReadOnlyList<string>> Directives, string Nonce) ReadPolicy(HttpResponseMessage response)
    {
        Assert.True(
            response.Headers.TryGetValues(HrWebContentSecurityPolicy.EnforceHeaderName, out var values),
            $"{response.RequestMessage?.RequestUri} ({(int)response.StatusCode}) has no Content-Security-Policy header.");
        Assert.False(response.Headers.Contains(HrWebContentSecurityPolicy.ReportOnlyHeaderName));

        var header = Assert.Single(values);
        var directives = ContentSecurityPolicyBuilder.Parse(header);
        var nonceSource = Assert.Single(directives["script-src"], s => s.StartsWith("'nonce-", StringComparison.Ordinal));
        return (directives, nonceSource["'nonce-".Length..^1]);
    }

    private static void AssertHardeningHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    public static TheoryData<string, HttpStatusCode> ProductionResponses => new()
    {
        { "/login", HttpStatusCode.OK },                                   // Razor page (App.razor)
        { "/app.js", HttpStatusCode.OK },                                  // static asset (MapStaticAssets)
        { "/verify-email", HttpStatusCode.OK },                            // minimal-API inline-script page
        { "/reset-password", HttpStatusCode.OK },
        { "/platform-admin/activate", HttpStatusCode.OK },
        { "/this/route/does/not/exist", HttpStatusCode.NotFound },         // re-executed /not-found page
        // Unhandled exception → UseExceptionHandler re-executes /Error. That page inherits Pages/_Imports'
        // [Authorize], so for this anonymous request the re-execution is challenged to /login (302).
        { ThrowPath, HttpStatusCode.Found },
    };

    [Theory]
    [MemberData(nameof(ProductionResponses))]
    public async Task Every_Response_Carries_The_Production_Policy_And_Hardening_Headers(string path, HttpStatusCode expectedStatus)
    {
        using var response = await Client(_production).GetAsync(path);

        Assert.Equal(expectedStatus, response.StatusCode);
        var (directives, _) = ReadPolicy(response);
        AssertHardeningHeaders(response);

        Assert.Equal(new[] { "'self'" }, directives["default-src"]);
        Assert.DoesNotContain("'unsafe-inline'", directives["script-src"]);
        Assert.DoesNotContain("'unsafe-eval'", directives["script-src"]);
        Assert.Equal(new[] { "'none'" }, directives["frame-ancestors"]);
        Assert.Equal(new[] { "'self'", "data:", SupabaseOrigin }, directives["img-src"]);
        Assert.Equal(new[] { "'self'", $"wss://{ProductionHost}" }, directives["connect-src"]);
        Assert.DoesNotContain(directives.Values.SelectMany(v => v), s => s.Contains("localhost", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Redirect_Responses_Also_Carry_The_Policy()
    {
        using var response = await Client(_production).GetAsync("/logout");

        Assert.True((int)response.StatusCode is >= 300 and < 400, $"Expected a redirect, got {(int)response.StatusCode}.");
        ReadPolicy(response);
        AssertHardeningHeaders(response);
    }

    [Fact]
    public async Task The_Cv_Proxy_Route_Alone_Allows_Same_Origin_Framing()
    {
        var path = $"/companies/{Guid.NewGuid()}/candidates/{Guid.NewGuid()}/cv/{Guid.NewGuid()}";
        using var cv = await Client(_production).GetAsync(path);
        using var page = await Client(_production).GetAsync("/login");

        Assert.Equal(new[] { "'self'" }, ReadPolicy(cv).Directives["frame-ancestors"]);
        Assert.Equal(new[] { "'self'" }, ReadPolicy(cv).Directives["object-src"]);
        Assert.Equal(new[] { "'none'" }, ReadPolicy(page).Directives["frame-ancestors"]);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Each_Response_Gets_A_Fresh_Nonce(string environment)
    {
        var client = Client(environment == "Production" ? _production : _development);

        var nonces = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.GetAsync("/login");
            nonces.Add(ReadPolicy(response).Nonce);
        }

        Assert.Equal(nonces.Count, nonces.Distinct(StringComparer.Ordinal).Count());
        Assert.All(nonces, n => Assert.Equal(16, Convert.FromBase64String(n).Length));
    }

    [Theory]
    [InlineData("Production", "/login")]
    [InlineData("Development", "/login")]
    [InlineData("Production", "/this/route/does/not/exist")]
    [InlineData("Production", "/verify-email")]
    [InlineData("Production", "/reset-password")]
    [InlineData("Production", "/platform-admin/activate")]
    public async Task Every_Inline_Script_Carries_This_Responses_Nonce(string environment, string path)
    {
        using var response = await Client(environment == "Production" ? _production : _development).GetAsync(path);
        var (_, nonce) = ReadPolicy(response);
        var html = await response.Content.ReadAsStringAsync();

        var inlineScripts = ScriptTag().Matches(html)
            .Select(m => m.Groups["attrs"].Value)
            .Where(attrs => !SrcAttribute().IsMatch(attrs))
            .ToList();

        Assert.NotEmpty(inlineScripts);
        Assert.All(inlineScripts, attrs => Assert.Equal(nonce, NonceAttribute().Match(attrs).Groups["nonce"].Value));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task App_Razor_Applies_The_Nonce_To_The_Theme_Script_And_The_Import_Map(string environment)
    {
        using var response = await Client(environment == "Production" ? _production : _development).GetAsync("/login");
        var (_, nonce) = ReadPolicy(response);
        var html = await response.Content.ReadAsStringAsync();

        var themeScript = Assert.Single(
            ScriptBlock().Matches(html),
            m => m.Groups["body"].Value.Contains("localStorage.getItem('theme')", StringComparison.Ordinal));
        Assert.Equal(nonce, NonceAttribute().Match(themeScript.Groups["attrs"].Value).Groups["nonce"].Value);

        var importMap = Assert.Single(
            ScriptTag().Matches(html),
            m => m.Groups["attrs"].Value.Contains("importmap", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(nonce, NonceAttribute().Match(importMap.Groups["attrs"].Value).Groups["nonce"].Value);
    }

    [Fact]
    public async Task Development_Adds_Only_The_Localhost_Tooling_Allowances()
    {
        using var response = await Client(_development).GetAsync("/login");
        var (directives, _) = ReadPolicy(response);

        Assert.Contains("http://localhost:*", directives["script-src"]);
        Assert.Contains("ws://localhost:*", directives["connect-src"]);
        Assert.DoesNotContain("'unsafe-inline'", directives["script-src"]);
        Assert.DoesNotContain("'unsafe-eval'", directives["script-src"]);
        Assert.Equal(new[] { "'none'" }, directives["frame-ancestors"]);
    }

    [Fact]
    public async Task ReportOnly_Configuration_Switches_The_Header_Name()
    {
        using var factory = new HrWebFactory("Production", reportOnly: true);
        using var response = await Client(factory).GetAsync("/login");

        // Report-only must not lose clickjacking protection: the only ENFORCED policy left is Blazor's
        // own frame-ancestors (kept on purpose in Program.cs for this mode).
        var enforced = Assert.Single(response.Headers.GetValues(HrWebContentSecurityPolicy.EnforceHeaderName));
        Assert.Equal(new[] { "frame-ancestors" }, ContentSecurityPolicyBuilder.Parse(enforced).Keys.ToArray());
        var header = Assert.Single(response.Headers.GetValues(HrWebContentSecurityPolicy.ReportOnlyHeaderName));
        Assert.DoesNotContain("'unsafe-inline'", ContentSecurityPolicyBuilder.Parse(header)["script-src"]);
    }

    [GeneratedRegex(@"<script\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"<script\b(?<attrs>[^>]*)>(?<body>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptBlock();

    [GeneratedRegex(@"\bsrc\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex SrcAttribute();

    [GeneratedRegex(@"\bnonce\s*=\s*""(?<nonce>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex NonceAttribute();

    private sealed class HrWebFactory(string environment, bool reportOnly = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            // HR.Web resolves the hrapi base address from Aspire service discovery config; nothing in
            // these tests reaches it (a dead local port keeps any accidental call fast and offline).
            builder.UseSetting("services:api:http:0", "http://127.0.0.1:9");
            builder.UseSetting("ContentSecurityPolicy:ImageOrigins:0", SupabaseOrigin);
            builder.UseSetting("ContentSecurityPolicy:ReportOnly", reportOnly ? "true" : "false");

            // Appended after HR.Web's own pipeline, so it only runs for a request no endpoint handled:
            // throws for ThrowPath to exercise UseExceptionHandler("/Error") re-execution.
            builder.ConfigureServices(services => services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, ThrowingStartupFilter>());
        }
    }

    private sealed class ThrowingStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path.Equals(ThrowPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("CSP pipeline test: forced failure.");
                await nextMiddleware(context);
            });
        };
    }
}
