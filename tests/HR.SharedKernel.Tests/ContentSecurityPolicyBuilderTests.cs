using HR.SharedKernel.Http;

namespace HR.SharedKernel.Tests;

public class ContentSecurityPolicyBuilderTests
{
    [Fact]
    public void Build_Serialises_Directives_In_Order_And_Round_Trips_Through_Parse()
    {
        var policy = new ContentSecurityPolicyBuilder()
            .Add("default-src", "'self'")
            .Add("img-src", "'self'", "data:", "https://cdn.example.com")
            .Add("upgrade-insecure-requests")
            .Build();

        Assert.Equal("default-src 'self'; img-src 'self' data: https://cdn.example.com; upgrade-insecure-requests", policy);

        var parsed = ContentSecurityPolicyBuilder.Parse(policy);
        Assert.Equal(new[] { "default-src", "img-src", "upgrade-insecure-requests" }, parsed.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "'self'", "data:", "https://cdn.example.com" }, parsed["img-src"]);
        Assert.Empty(parsed["upgrade-insecure-requests"]);
    }

    [Fact]
    public void Add_Rejects_A_Duplicate_Directive()
    {
        var builder = new ContentSecurityPolicyBuilder().Add("script-src", "'self'");

        Assert.Throws<InvalidOperationException>(() => builder.Add("script-src", "'none'"));
        Assert.Throws<InvalidOperationException>(() => builder.Add("Script-Src", "'none'"));
    }

    [Theory]
    [InlineData("https://a.example.com; script-src *")]
    [InlineData("https://a.example.com https://b.example.com")]
    [InlineData("https://a.example.com,https://b.example.com")]
    [InlineData("")]
    [InlineData("https://a.example.com\n")]
    public void Add_Rejects_Sources_That_Could_Inject_Extra_Sources_Or_Directives(string source)
    {
        Assert.Throws<ArgumentException>(() => new ContentSecurityPolicyBuilder().Add("img-src", source));
    }

    [Theory]
    [InlineData("script src")]
    [InlineData("script-src;")]
    [InlineData("Script-SRC123")]
    public void Add_Rejects_Invalid_Directive_Names(string directive)
    {
        Assert.Throws<ArgumentException>(() => new ContentSecurityPolicyBuilder().Add(directive, "'self'"));
    }

    [Fact]
    public void Parse_Rejects_A_Repeated_Directive_Which_Browsers_Would_Ignore()
    {
        Assert.Throws<FormatException>(() => ContentSecurityPolicyBuilder.Parse("script-src 'self'; script-src 'unsafe-inline'"));
    }

    [Fact]
    public void CreateNonce_Returns_Distinct_128_Bit_Base64_Values()
    {
        var nonces = Enumerable.Range(0, 100).Select(_ => ContentSecurityPolicyBuilder.CreateNonce()).ToList();

        Assert.Equal(nonces.Count, nonces.Distinct(StringComparer.Ordinal).Count());
        Assert.All(nonces, n => Assert.Equal(16, Convert.FromBase64String(n).Length));
    }

    [Fact]
    public void NonceSource_Quotes_The_Nonce_And_Rejects_Non_Base64_Input()
    {
        Assert.Equal("'nonce-abc123+/='", ContentSecurityPolicyBuilder.NonceSource("abc123+/="));
        Assert.Throws<ArgumentException>(() => ContentSecurityPolicyBuilder.NonceSource("abc' 'unsafe-inline"));
        Assert.ThrowsAny<ArgumentException>(() => ContentSecurityPolicyBuilder.NonceSource(" "));
    }
}
