using System.Text.Json;
using HR.Marketing.Seo;

namespace HR.Marketing.Tests;

public class StructuredDataScriptTests
{
    private static readonly Dictionary<string, string> Tokens = new(StringComparer.Ordinal)
    {
        ["__SITE_URL__"] = "https://onebigteam.com",
        ["__CANONICAL_URL__"] = "https://onebigteam.com/pricing",
    };

    [Fact]
    public void Build_replaces_tokens_and_produces_valid_json()
    {
        const string raw = """{ "@context": "https://schema.org", "url": "__SITE_URL__", "page": "__CANONICAL_URL__" }""";

        var result = StructuredDataScript.Build(raw, Tokens);

        Assert.NotNull(result);
        using var doc = JsonDocument.Parse(result!);
        Assert.Equal("https://onebigteam.com", doc.RootElement.GetProperty("url").GetString());
        Assert.Equal("https://onebigteam.com/pricing", doc.RootElement.GetProperty("page").GetString());
    }

    [Fact]
    public void Build_escapes_angle_brackets_so_script_cannot_break_out()
    {
        const string raw = """{ "name": "</script><script>alert(1)</script>" }""";

        var result = StructuredDataScript.Build(raw, Tokens);

        Assert.NotNull(result);
        Assert.DoesNotContain("</script>", result!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script>", result!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\u003C", result!); // '<' escaped
        // Still valid JSON with the original logical value preserved.
        using var doc = JsonDocument.Parse(result!);
        Assert.Equal("</script><script>alert(1)</script>", doc.RootElement.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("</script><script>alert(1)</script>")]
    [InlineData("not json at all")]
    [InlineData("{ \"broken\": ")]
    [InlineData("<!-- comment -->")]
    [InlineData("{}]]>")]
    [InlineData("{ \"a\": 1 } trailing")]
    public void Build_returns_null_for_non_json_or_breakout_input(string raw)
    {
        Assert.Null(StructuredDataScript.Build(raw, Tokens));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_returns_null_for_blank_input(string? raw)
    {
        Assert.Null(StructuredDataScript.Build(raw, Tokens));
    }

    [Fact]
    public void Build_neutralises_hostile_token_value()
    {
        var hostileTokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["__SITE_URL__"] = "\"</script><script>alert(1)</script>",
        };
        const string raw = """{ "url": "__SITE_URL__" }""";

        var result = StructuredDataScript.Build(raw, hostileTokens);

        Assert.NotNull(result);
        Assert.DoesNotContain("</script>", result!, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(result!);
        Assert.Equal("\"</script><script>alert(1)</script>", doc.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public void Build_preserves_realistic_faq_json_ld()
    {
        const string raw = """
            {
              "@context": "https://schema.org",
              "@type": "FAQPage",
              "mainEntity": [
                { "@type": "Question", "name": "Q?", "acceptedAnswer": { "@type": "Answer", "text": "A." } }
              ]
            }
            """;

        var result = StructuredDataScript.Build(raw, Tokens);

        Assert.NotNull(result);
        using var doc = JsonDocument.Parse(result!);
        Assert.Equal("FAQPage", doc.RootElement.GetProperty("@type").GetString());
    }
}
