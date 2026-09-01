using HR.Web.Services;

namespace HR.Web.Tests;

public class SupportHtmlSanitizerTests
{
    [Theory]
    [InlineData("<script>alert('x')</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<a href=\"javascript:alert(1)\">click</a>")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>")]
    [InlineData("<div onclick=\"steal()\">hi</div>")]
    [InlineData("<style>body{display:none}</style>")]
    [InlineData("<body onload=alert(1)>")]
    public void Sanitize_strips_executable_content(string malicious)
    {
        var result = SupportHtmlSanitizer.Sanitize(malicious);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_handles_unclosed_and_malformed_tags()
    {
        var result = SupportHtmlSanitizer.Sanitize("<b>bold <i>and italic <script>bad");

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bold", result);
        Assert.Contains("and italic", result);
    }

    [Fact]
    public void Sanitize_decodes_then_strips_encoded_payloads()
    {
        // Entity-encoded "<script>" — the parser decodes it; it must not resurface as a live tag.
        var result = SupportHtmlSanitizer.Sanitize("&lt;script&gt;alert(1)&lt;/script&gt;");

        Assert.DoesNotContain("<script>", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_preserves_basic_formatting()
    {
        var result = SupportHtmlSanitizer.Sanitize(
            "<p>Hello <strong>team</strong>, see <a href=\"https://example.com/help\">the docs</a>.</p><ul><li>one</li></ul>");

        Assert.Contains("<p>", result);
        Assert.Contains("<strong>team</strong>", result);
        Assert.Contains("<li>one</li>", result);
        Assert.Contains("href=\"https://example.com/help\"", result);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", result);
    }

    [Fact]
    public void Sanitize_keeps_plain_text_reply_intact()
    {
        const string plain = "Thanks for the update. I tried again and it works now.";

        Assert.Equal(plain, SupportHtmlSanitizer.Sanitize(plain));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sanitize_returns_empty_for_blank_input(string? input)
    {
        Assert.Equal(string.Empty, SupportHtmlSanitizer.Sanitize(input));
    }
}
