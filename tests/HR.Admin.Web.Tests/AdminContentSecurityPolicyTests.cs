using System.Text.RegularExpressions;
using HR.Admin.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Admin.Web.Tests;

/// <summary>
/// P1 support-conversation stored-XSS fix — secondary mitigation: the Admin Portal's Content
/// Security Policy must block inline script/event handlers (nonce-only inline script, no
/// 'unsafe-inline' in script-src), plugins, frames and base/form hijacking, and must not leak
/// development-only localhost sources into production.
/// </summary>
public class AdminContentSecurityPolicyTests
{
    private const string Nonce = "dGVzdC1ub25jZS0xMjM0NQ==";

    private static readonly Regex NonceInHeader = new("'nonce-([^']+)'", RegexOptions.Compiled);

    private static Dictionary<string, string[]> ParseDirectives(string policy) =>
        policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(parts => parts[0], parts => parts[1..], StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScriptSrc_Uses_The_Nonce_And_Never_Allows_Unsafe_Inline(bool isDevelopment)
    {
        var directives = ParseDirectives(AdminContentSecurityPolicy.Build(Nonce, isDevelopment));

        var scriptSrc = directives["script-src"];
        Assert.Contains($"'nonce-{Nonce}'", scriptSrc);
        Assert.Contains("'self'", scriptSrc);
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc);
        Assert.DoesNotContain("*", scriptSrc);
        Assert.DoesNotContain("data:", scriptSrc);
        Assert.DoesNotContain("https:", scriptSrc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Policy_Contains_The_Hardening_Directives(bool isDevelopment)
    {
        var directives = ParseDirectives(AdminContentSecurityPolicy.Build(Nonce, isDevelopment));

        Assert.Equal(new[] { "'self'" }, directives["default-src"]);
        Assert.Equal(new[] { "'none'" }, directives["object-src"]);
        Assert.Equal(new[] { "'none'" }, directives["frame-src"]);
        Assert.Equal(new[] { "'none'" }, directives["frame-ancestors"]);
        Assert.Equal(new[] { "'self'" }, directives["base-uri"]);
        Assert.Equal(new[] { "'self'" }, directives["form-action"]);
    }

    [Fact]
    public void Production_Policy_Contains_No_Localhost_Sources()
    {
        var policy = AdminContentSecurityPolicy.Build(Nonce, isDevelopment: false);

        Assert.DoesNotContain("localhost", policy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ws:", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "'self'" }, ParseDirectives(policy)["connect-src"]);
    }

    [Fact]
    public void Development_Policy_Allows_Localhost_For_Browser_Refresh()
    {
        var directives = ParseDirectives(AdminContentSecurityPolicy.Build(Nonce, isDevelopment: true));

        Assert.Contains(directives["script-src"], s => s.Contains("localhost", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(directives["connect-src"], s => s.StartsWith("ws://localhost", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(directives["connect-src"], s => s.StartsWith("wss://localhost", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Development_Relaxation_Does_Not_Touch_The_Hardening_Directives()
    {
        var production = ParseDirectives(AdminContentSecurityPolicy.Build(Nonce, isDevelopment: false));
        var development = ParseDirectives(AdminContentSecurityPolicy.Build(Nonce, isDevelopment: true));

        foreach (var directive in new[] { "default-src", "object-src", "frame-src", "frame-ancestors", "base-uri", "form-action", "style-src", "img-src", "font-src" })
            Assert.Equal(production[directive], development[directive]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_Throws_For_Blank_Nonce(string? nonce)
    {
        Assert.ThrowsAny<ArgumentException>(() => AdminContentSecurityPolicy.Build(nonce!, isDevelopment: false));
    }

    [Fact]
    public void CreateNonce_Returns_Distinct_Base64_Encoded_16_Byte_Values()
    {
        var nonces = Enumerable.Range(0, 50).Select(_ => AdminContentSecurityPolicy.CreateNonce()).ToList();

        Assert.Equal(nonces.Count, nonces.Distinct(StringComparer.Ordinal).Count());
        Assert.All(nonces, n => Assert.Equal(16, Convert.FromBase64String(n).Length));
    }

    [Fact]
    public void GetNonce_Returns_Null_For_Null_Context_Or_Missing_Item()
    {
        Assert.Null(AdminContentSecurityPolicy.GetNonce(null));
        Assert.Null(AdminContentSecurityPolicy.GetNonce(new DefaultHttpContext()));
    }

    [Fact]
    public void GetNonce_Returns_Null_When_Item_Is_Not_A_String()
    {
        var context = new DefaultHttpContext();
        context.Items[AdminContentSecurityPolicy.NonceItemKey] = 42;

        Assert.Null(AdminContentSecurityPolicy.GetNonce(context));
    }

    private static async Task<(HttpContext Context, string? NonceSeenDownstream)> InvokeMiddlewareAsync(bool isDevelopment)
    {
        string? captured = null;
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseAdminContentSecurityPolicy(isDevelopment);
        app.Run(ctx =>
        {
            captured = AdminContentSecurityPolicy.GetNonce(ctx);
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        await app.Build()(context);
        return (context, captured);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Middleware_Sets_Header_And_Exposes_The_Same_Nonce_Downstream(bool isDevelopment)
    {
        var (context, downstreamNonce) = await InvokeMiddlewareAsync(isDevelopment);

        var header = context.Response.Headers[AdminContentSecurityPolicy.HeaderName].ToString();
        Assert.False(string.IsNullOrWhiteSpace(header));

        var match = NonceInHeader.Match(header);
        Assert.True(match.Success, $"Expected a nonce source in the CSP header: {header}");

        Assert.NotNull(downstreamNonce);
        Assert.Equal(downstreamNonce, match.Groups[1].Value);
        Assert.Equal(downstreamNonce, context.Items[AdminContentSecurityPolicy.NonceItemKey]);
        Assert.Equal(AdminContentSecurityPolicy.Build(downstreamNonce!, isDevelopment), header);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public async Task Middleware_Issues_A_Fresh_Nonce_Per_Request()
    {
        var (_, first) = await InvokeMiddlewareAsync(isDevelopment: false);
        var (_, second) = await InvokeMiddlewareAsync(isDevelopment: false);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }
}
