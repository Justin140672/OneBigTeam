using HR.SharedKernel.Http;
using HR.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HR.Web.Tests;

/// <summary>
/// [P2] Content Security Policy for HR.Web — directive-level assertions on the parsed header (never
/// substring checks on the raw string), covering the production policy, the development-only
/// allowances and the configuration guard rails. Pipeline behaviour (every response, fresh nonce,
/// nonce applied to App.razor's inline scripts) is covered by
/// <see cref="HrWebContentSecurityPolicyPipelineTests"/>.
/// </summary>
public class HrWebContentSecurityPolicyTests
{
    private const string Nonce = "dGVzdC1ub25jZS0xMjM0NQ==";
    private const string ProductionHost = "app.onebigteam.example";
    private const string SupabaseOrigin = "https://abcdefgh.supabase.co";

    private static HrWebCspSettings Settings(string environmentName, params (string Key, string? Value)[] config) =>
        HrWebCspSettings.Create(
            new TestHostEnvironment(environmentName),
            new ConfigurationBuilder()
                .AddInMemoryCollection(config.Select(c => new KeyValuePair<string, string?>(c.Key, c.Value)))
                .Build());

    private static HrWebCspSettings Production(params (string Key, string? Value)[] config) => Settings(Environments.Production, config);

    private static HrWebCspSettings Development(params (string Key, string? Value)[] config) => Settings(Environments.Development, config);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Parse(HrWebCspSettings settings, string? host = ProductionHost, bool allowFraming = false) =>
        ContentSecurityPolicyBuilder.Parse(HrWebContentSecurityPolicy.Build(Nonce, settings, host, allowFraming));

    private static bool IsDevelopmentHost(string source) =>
        source.Contains("localhost", StringComparison.OrdinalIgnoreCase)
        || source.Contains("127.0.0.1", StringComparison.Ordinal)
        || source.Contains("[::1]", StringComparison.Ordinal)
        || source.StartsWith("ws:", StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("http:", StringComparison.OrdinalIgnoreCase);

    public static TheoryData<string> AllEnvironments => new() { Environments.Production, Environments.Staging, Environments.Development };

    [Theory]
    [MemberData(nameof(AllEnvironments))]
    public void ScriptSrc_Is_Self_Plus_Nonce_With_No_Unsafe_Inline_Or_Unsafe_Eval(string environmentName)
    {
        var scriptSrc = Parse(Settings(environmentName))["script-src"];

        Assert.Equal("'self'", scriptSrc[0]);
        Assert.Equal($"'nonce-{Nonce}'", scriptSrc[1]);
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc);
        Assert.DoesNotContain("'unsafe-eval'", scriptSrc);
        Assert.DoesNotContain("'unsafe-hashes'", scriptSrc);
        Assert.DoesNotContain("'strict-dynamic'", scriptSrc);
        Assert.DoesNotContain("data:", scriptSrc);
        Assert.DoesNotContain("blob:", scriptSrc);
        Assert.DoesNotContain("https:", scriptSrc);
        Assert.DoesNotContain("*", scriptSrc);
    }

    [Fact]
    public void Production_Policy_Has_The_Exact_Expected_Directives()
    {
        var directives = Parse(Production(("ContentSecurityPolicy:ImageOrigins:0", SupabaseOrigin)));

        Assert.Equal(
            new[] { "base-uri", "connect-src", "default-src", "font-src", "form-action", "frame-ancestors", "frame-src", "img-src", "object-src", "script-src", "style-src" },
            directives.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

        Assert.Equal(new[] { "'self'" }, directives["default-src"]);
        Assert.Equal(new[] { "'self'", $"'nonce-{Nonce}'" }, directives["script-src"]);
        Assert.Equal(new[] { "'self'", "'unsafe-inline'", "https://fonts.googleapis.com", "https://cdn.jsdelivr.net" }, directives["style-src"]);
        Assert.Equal(new[] { "'self'", "data:", "https://fonts.gstatic.com" }, directives["font-src"]);
        Assert.Equal(new[] { "'self'", "data:", SupabaseOrigin }, directives["img-src"]);
        Assert.Equal(new[] { "'self'", $"wss://{ProductionHost}" }, directives["connect-src"]);
        Assert.Equal(new[] { "'self'" }, directives["object-src"]);
        Assert.Equal(new[] { "'self'" }, directives["frame-src"]);
        Assert.Equal(new[] { "'none'" }, directives["frame-ancestors"]);
        Assert.Equal(new[] { "'self'" }, directives["base-uri"]);
        Assert.Equal(new[] { "'self'" }, directives["form-action"]);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("E2E")]
    public void Non_Development_Policy_Contains_No_Development_Hosts(string environmentName)
    {
        var directives = Parse(Settings(environmentName, ("ContentSecurityPolicy:ImageOrigins:0", SupabaseOrigin)));

        foreach (var (directive, sources) in directives)
        {
            Assert.DoesNotContain(sources, IsDevelopmentHost);
            Assert.DoesNotContain(sources, s => s.Contains('*'));
        }
    }

    [Fact]
    public void Development_Relaxations_Are_Added_Only_Where_Needed()
    {
        var production = Parse(Production());
        var development = Parse(Development());

        Assert.Equal(new[] { "'self'", $"'nonce-{Nonce}'", "http://localhost:*", "https://localhost:*" }, development["script-src"]);
        Assert.Equal(
            new[] { "'self'", $"wss://{ProductionHost}", "ws://localhost:*", "wss://localhost:*", "http://localhost:*", "https://localhost:*" },
            development["connect-src"]);
        Assert.Equal(new[] { "'self'", "data:", "http://localhost:*", "https://localhost:*" }, development["img-src"]);

        foreach (var directive in new[] { "default-src", "style-src", "font-src", "object-src", "frame-src", "frame-ancestors", "base-uri", "form-action" })
            Assert.Equal(production[directive], development[directive]);
    }

    [Fact]
    public void No_Configuration_Key_Can_Switch_On_The_Development_Allowances()
    {
        var settings = Production(
            ("ContentSecurityPolicy:IsDevelopment", "true"),
            ("ContentSecurityPolicy:Development", "true"),
            ("ASPNETCORE_ENVIRONMENT", "Development"),
            ("DOTNET_ENVIRONMENT", "Development"));

        Assert.False(settings.IsDevelopment);
        Assert.DoesNotContain(Parse(settings).Values.SelectMany(v => v), IsDevelopmentHost);
    }

    [Fact]
    public void Only_The_Framed_Endpoint_Relaxes_Frame_Ancestors_And_Only_To_Self()
    {
        Assert.Equal(new[] { "'none'" }, Parse(Production(), allowFraming: false)["frame-ancestors"]);
        Assert.Equal(new[] { "'self'" }, Parse(Production(), allowFraming: true)["frame-ancestors"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("evil.example.com; script-src *")]
    [InlineData("evil.example.com 'unsafe-inline'")]
    [InlineData("evil.example.com,x")]
    [InlineData("user@evil.example.com")]
    [InlineData("-bad-.example.com")]
    public void Connect_Src_Ignores_A_Missing_Or_Malformed_Host(string? host)
    {
        var connectSrc = Parse(Production(), host)["connect-src"];

        Assert.Equal(new[] { "'self'" }, connectSrc);
    }

    [Fact]
    public void Connect_Src_Keeps_A_Host_With_A_Port()
    {
        Assert.Equal(new[] { "'self'", "wss://app.example.com:8443" }, Parse(Production(), "App.Example.com:8443")["connect-src"]);
    }

    [Fact]
    public void Image_Origins_Are_Normalised_And_Deduplicated()
    {
        var settings = Production(
            ("ContentSecurityPolicy:ImageOrigins:0", "https://ABCDEFGH.supabase.co/"),
            ("ContentSecurityPolicy:ImageOrigins:1", "https://abcdefgh.supabase.co"),
            ("ContentSecurityPolicy:ImageOrigins:2", "  "));

        Assert.Equal(new[] { SupabaseOrigin }, settings.ImageOrigins);
    }

    [Theory]
    [InlineData("https://*.supabase.co")]
    [InlineData("*")]
    [InlineData("http://abcdefgh.supabase.co")]
    [InlineData("https://localhost:7001")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://[::1]:5001")]
    [InlineData("https://storage.localhost")]
    [InlineData("https://abcdefgh.supabase.co/storage/v1")]
    [InlineData("https://abcdefgh.supabase.co?x=1")]
    [InlineData("https://user:pass@abcdefgh.supabase.co")]
    [InlineData("abcdefgh.supabase.co")]
    [InlineData("data:")]
    public void Production_Rejects_Unsafe_Image_Origins_At_Startup(string origin)
    {
        Assert.Throws<InvalidOperationException>(() => Production(("ContentSecurityPolicy:ImageOrigins:0", origin)));
    }

    [Fact]
    public void Development_Accepts_A_Localhost_Image_Origin()
    {
        var settings = Development(("ContentSecurityPolicy:ImageOrigins:0", "http://localhost:5410"));

        Assert.Equal(new[] { "http://localhost:5410" }, settings.ImageOrigins);
    }

    [Theory]
    [InlineData(null, false, "Content-Security-Policy")]
    [InlineData("false", false, "Content-Security-Policy")]
    [InlineData("true", true, "Content-Security-Policy-Report-Only")]
    public void ReportOnly_Defaults_To_Enforce(string? configured, bool expectedReportOnly, string expectedHeader)
    {
        var settings = Production(("ContentSecurityPolicy:ReportOnly", configured));

        Assert.Equal(expectedReportOnly, settings.ReportOnly);
        Assert.Equal(expectedHeader, settings.HeaderName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Build_Throws_For_A_Blank_Nonce(string? nonce)
    {
        Assert.ThrowsAny<ArgumentException>(() => HrWebContentSecurityPolicy.Build(nonce!, Production(), ProductionHost));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "HR.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
