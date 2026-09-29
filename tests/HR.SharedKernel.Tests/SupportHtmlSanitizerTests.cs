using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using HR.SharedKernel.Html;

namespace HR.SharedKernel.Tests;

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

    [Theory]
    [InlineData("<svg onload=alert(1)><circle/></svg>")]
    [InlineData("<svg><script>alert(1)</script></svg>")]
    [InlineData("<math><mtext><script>alert(1)</script></mtext></math>")]
    public void Sanitize_strips_svg_and_mathml_payloads(string malicious)
    {
        var result = SupportHtmlSanitizer.Sanitize(malicious);

        Assert.DoesNotContain("<svg", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<math", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<a href=\"data:text/html,<script>alert(1)</script>\">click</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">click</a>")]
    [InlineData("<a HREF=\"javascript:alert(1)\" OnClick=\"steal()\">click</a>")]
    public void Sanitize_strips_dangerous_schemes_and_mixed_case_attributes(string malicious)
    {
        var result = SupportHtmlSanitizer.Sanitize(malicious);

        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_strips_mixed_case_script_tags()
    {
        var result = SupportHtmlSanitizer.Sanitize("<ScRiPt>alert(1)</sCrIpT>");

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
    }


    private static readonly Regex SchemePrefix = new(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:", RegexOptions.Compiled);

    private static void AssertStructurallySafe(string output)
    {
        var document = new HtmlParser().ParseDocument(output);
        var body = document.Body;
        Assert.NotNull(body);

        Assert.Empty(document.Head!.Children);

        foreach (var element in body!.QuerySelectorAll("*"))
        {
            Assert.True(SupportHtmlSanitizer.AllowedTags.Contains(element.LocalName),
                $"Element <{element.LocalName}> is not on the allow-list. Output: {output}");

            foreach (var attribute in element.Attributes)
            {
                var name = attribute.Name;
                Assert.False(name.StartsWith("on", StringComparison.OrdinalIgnoreCase),
                    $"Event-handler attribute '{name}' survived. Output: {output}");
                Assert.False(name.StartsWith("data-", StringComparison.OrdinalIgnoreCase),
                    $"data-* attribute '{name}' survived. Output: {output}");
                Assert.DoesNotContain(name, new[] { "style", "class", "id", "formaction", "src", "action" },
                    StringComparer.OrdinalIgnoreCase);

                var isForcedLinkAttribute = element.LocalName == "a"
                    && (name.Equals("rel", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("target", StringComparison.OrdinalIgnoreCase));
                Assert.True(SupportHtmlSanitizer.AllowedAttributes.Contains(name) || isForcedLinkAttribute,
                    $"Attribute '{name}' on <{element.LocalName}> is not on the allow-list. Output: {output}");
            }

            var href = element.GetAttribute("href");
            if (href is not null)
                AssertHrefIsSafe(href, output);
        }
    }

    private static void AssertHrefIsSafe(string href, string output)
    {
        var normalised = new string(href.Trim().Where(c => c is not ('\t' or '\n' or '\r')).ToArray());

        if (!SchemePrefix.IsMatch(normalised))
            return;

        Assert.True(
            normalised.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
            || normalised.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase),
            $"href '{href}' uses a scheme outside the allow-list. Output: {output}");
    }

    // ── Rejected elements ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<script>alert(1)</script>", "script")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>", "iframe")]
    [InlineData("<object data=\"x.swf\"></object>", "object")]
    [InlineData("<embed src=\"x.swf\">", "embed")]
    [InlineData("<svg onload=alert(1)><circle/></svg>", "svg")]
    [InlineData("<svg><script>alert(1)</script></svg>", "svg")]
    [InlineData("<math><mtext><script>alert(1)</script></mtext></math>", "math")]
    [InlineData("<img src=x onerror=alert(1)>", "img")]
    [InlineData("<style>body{display:none}</style>", "style")]
    [InlineData("<link rel=\"stylesheet\" href=\"https://evil.example/x.css\">", "link")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://evil.example\">", "meta")]
    [InlineData("<base href=\"https://evil.example/\">", "base")]
    [InlineData("<form action=\"https://evil.example\"><input name=\"x\"><button formaction=\"javascript:alert(1)\">Go</button></form>", "form")]
    [InlineData("<video src=x onerror=alert(1)></video>", "video")]
    [InlineData("<audio src=x onerror=alert(1)></audio>", "audio")]
    [InlineData("<template><script>alert(1)</script></template>", "template")]
    [InlineData("<noscript><p>hi</p></noscript>", "noscript")]
    public void Sanitize_removes_disallowed_elements(string malicious, string forbiddenTag)
    {
        var result = SupportHtmlSanitizer.Sanitize(malicious);

        AssertStructurallySafe(result);
        Assert.DoesNotContain("<" + forbiddenTag, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_removes_form_controls_but_keeps_their_visible_text_inert()
    {
        var result = SupportHtmlSanitizer.Sanitize(
            "<form action=\"https://evil.example\"><input name=\"x\"><button formaction=\"javascript:alert(1)\">Go</button></form>");

        AssertStructurallySafe(result);
        Assert.DoesNotContain("<input", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<button", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("formaction", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Go", result);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<ScRiPt>alert(1)</sCrIpT>")]
    [InlineData("<style>body{display:none}</style>")]
    [InlineData("<template><img src=x onerror=alert(1)></template>")]
    [InlineData("<noscript>alert(1)</noscript>")]
    [InlineData("<iframe>alert(1)</iframe>")]
    [InlineData("<svg><text>alert(1)</text></svg>")]
    [InlineData("<math><mtext>alert(1)</mtext></math>")]
    [InlineData("<textarea>alert(1)</textarea>")]
    public void Sanitize_discards_script_and_raw_text_element_content_entirely(string malicious)
    {
        var result = SupportHtmlSanitizer.Sanitize(malicious);

        AssertStructurallySafe(result);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Sanitize_drops_script_content_but_keeps_surrounding_reply_text()
    {
        var result = SupportHtmlSanitizer.Sanitize("<p>Before</p><script>alert(1)</script><p>After</p>");

        AssertStructurallySafe(result);
        Assert.Equal("<p>Before</p><p>After</p>", result);
    }

    // ── Rejected attributes ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<p style=\"color:red\">text</p>")]
    [InlineData("<p style=\"background:url(javascript:alert(1))\">text</p>")]
    [InlineData("<p class=\"danger\">text</p>")]
    [InlineData("<p id=\"main\">text</p>")]
    [InlineData("<p data-x=\"1\">text</p>")]
    [InlineData("<p onclick=\"alert(1)\">text</p>")]
    [InlineData("<p onmouseover=\"alert(1)\">text</p>")]
    [InlineData("<p OnErRoR=\"alert(1)\">text</p>")]
    [InlineData("<p ONCLICK=\"alert(1)\">text</p>")]
    [InlineData("<strong formaction=\"javascript:alert(1)\">text</strong>")]
    public void Sanitize_strips_disallowed_attributes_but_keeps_the_element(string malicious)
    {
        var result = SupportHtmlSanitizer.Sanitize(malicious);

        AssertStructurallySafe(result);
        Assert.Contains(">text<", result);
        Assert.DoesNotContain("alert", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("=", result);
    }

    [Fact]
    public void Sanitize_keeps_title_but_strips_other_attributes_on_the_same_element()
    {
        var result = SupportHtmlSanitizer.Sanitize("<p title=\"x\" onclick=\"y\" style=\"z\">k</p>");

        AssertStructurallySafe(result);
        Assert.Equal("<p title=\"x\">k</p>", result);
    }

    // ── Rejected URL schemes ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">x</a>")]
    [InlineData("<a href=\"&#106;avascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"&#x6A;avascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"javas&#99;ript:alert(1)\">x</a>")]
    [InlineData("<a href=\"java&#x09;script:alert(1)\">x</a>")]
    [InlineData("<a href=\"java&#x0A;script:alert(1)\">x</a>")]
    [InlineData("<a href=\"   javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"&#x20;javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"vbscript:msgbox(1)\">x</a>")]
    [InlineData("<a href=\"data:text/html,<script>alert(1)</script>\">x</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>")]
    [InlineData("<a href=\"http://example.com\">x</a>")]
    public void Sanitize_drops_href_with_a_disallowed_scheme_and_keeps_the_link_text(string malicious)
    {
        var result = SupportHtmlSanitizer.Sanitize(malicious);

        AssertStructurallySafe(result);
        Assert.Equal("<a>x</a>", result);
    }


    [Theory]
    [InlineData("<b>bold <i>and italic <script>bad")]
    [InlineData("<scr<script>ipt>alert(1)</script>")]
    [InlineData("<ScRiPt>alert(1)</sCrIpT>")]
    [InlineData("<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\"></noscript>")]
    [InlineData("<svg><style><img src=x onerror=alert(1)></style></svg>")]
    [InlineData("<p><svg><p><style><img src=x onerror=alert(1)></style></p></svg></p>")]
    [InlineData("<a href=\"https://example.com\"<img src=x onerror=alert(1)>>x</a>")]
    [InlineData("<<script>script>alert(1)<</script>/script>")]
    public void Sanitize_neutralises_malformed_and_mutation_payloads(string malicious)
    {
        var result = SupportHtmlSanitizer.Sanitize(malicious);

        AssertStructurallySafe(result);

        AssertStructurallySafe(SupportHtmlSanitizer.Sanitize(result));
    }

    [Fact]
    public void Sanitize_keeps_entity_encoded_markup_as_inert_escaped_text()
    {
        const string input = "&lt;script&gt;alert(1)&lt;/script&gt;";

        var result = SupportHtmlSanitizer.Sanitize(input);

        AssertStructurallySafe(result);
        Assert.Equal(input, result);
        var document = new HtmlParser().ParseDocument(result);
        Assert.Empty(document.Body!.Children);
        Assert.Equal("<script>alert(1)</script>", document.Body.TextContent);
    }

    [Fact]
    public void Sanitize_keeps_escaped_ampersands_and_angle_brackets_in_text()
    {
        const string input = "<p>Tom &amp; Jerry &lt;3</p>";

        Assert.Equal(input, SupportHtmlSanitizer.Sanitize(input));
    }


    [Theory]
    [InlineData("<p>para</p>")]
    [InlineData("<strong>strong</strong>")]
    [InlineData("<b>bold</b>")]
    [InlineData("<em>em</em>")]
    [InlineData("<i>italic</i>")]
    [InlineData("<u>underline</u>")]
    [InlineData("<s>strike</s>")]
    [InlineData("<span>span</span>")]
    [InlineData("<ul><li>one</li><li>two</li></ul>")]
    [InlineData("<ol><li>one</li></ol>")]
    [InlineData("<blockquote>quoted</blockquote>")]
    [InlineData("<h3>heading 3</h3>")]
    [InlineData("<h4>heading 4</h4>")]
    [InlineData("<h5>heading 5</h5>")]
    [InlineData("<p>line one<br>line two</p>")]
    [InlineData("<p>above</p><hr><p>below</p>")]
    [InlineData("<p title=\"A tooltip\">t</p>")]
    public void Sanitize_preserves_each_allowed_formatting_element_verbatim(string safe)
    {
        var result = SupportHtmlSanitizer.Sanitize(safe);

        AssertStructurallySafe(result);
        Assert.Equal(safe, result);
    }

    [Theory]
    [InlineData("<h1>h1</h1>", "h1")]
    [InlineData("<h2>h2</h2>", "h2")]
    [InlineData("<div>div</div>", "div")]
    [InlineData("<table><tr><td>td</td></tr></table>", "td")]
    public void Sanitize_unwraps_non_allow_listed_but_harmless_elements_to_their_text(string input, string expectedText)
    {
        var result = SupportHtmlSanitizer.Sanitize(input);

        AssertStructurallySafe(result);
        Assert.Equal(expectedText, result);
    }

    [Fact]
    public void Sanitize_keeps_code_content_as_escaped_text_inside_pre()
    {
        var result = SupportHtmlSanitizer.Sanitize("<pre><code>if (a < b) {}</code></pre>");

        AssertStructurallySafe(result);
        Assert.Equal("<pre><code>if (a &lt; b) {}</code></pre>", result);

        var code = new HtmlParser().ParseDocument(result).QuerySelector("pre > code");
        Assert.NotNull(code);
        Assert.Equal("if (a < b) {}", code!.TextContent);
        Assert.Empty(code.Children);
    }

    [Theory]
    [InlineData("https://example.com/help")]
    [InlineData("HTTPS://example.com")]
    [InlineData("mailto:support@example.com")]
    [InlineData("MAILTO:a@b.com")]
    [InlineData("/help/articles/1")]
    [InlineData("#section")]
    [InlineData("?q=1")]
    public void Sanitize_keeps_allowed_link_and_forces_safe_rel_and_target(string href)
    {
        var result = SupportHtmlSanitizer.Sanitize($"<a href=\"{href}\">link</a>");

        AssertStructurallySafe(result);
        var anchor = new HtmlParser().ParseDocument(result).QuerySelector("a");
        Assert.NotNull(anchor);
        Assert.Equal(href, anchor!.GetAttribute("href"));
        Assert.Equal(SupportHtmlSanitizer.LinkRel, anchor.GetAttribute("rel"));
        Assert.Equal("_blank", anchor.GetAttribute("target"));
        Assert.Equal("link", anchor.TextContent);
    }

    [Fact]
    public void Sanitize_overrides_author_supplied_rel_and_target()
    {
        var result = SupportHtmlSanitizer.Sanitize(
            "<a href=\"https://example.com\" target=\"_self\" rel=\"opener\">x</a>");

        AssertStructurallySafe(result);
        Assert.Equal(
            $"<a href=\"https://example.com\" rel=\"{SupportHtmlSanitizer.LinkRel}\" target=\"_blank\">x</a>",
            result);
    }

    [Fact]
    public void Sanitize_does_not_add_rel_or_target_to_anchor_without_href()
    {
        Assert.Equal("<a>no href</a>", SupportHtmlSanitizer.Sanitize("<a>no href</a>"));
    }

    [Fact]
    public void Sanitize_keeps_safe_parts_and_removes_malicious_parts_of_a_mixed_body()
    {
        var result = SupportHtmlSanitizer.Sanitize(
            "<p>Hi <strong>there</strong></p><script>alert(1)</script><img src=x onerror=alert(1)><a href=\"javascript:alert(1)\">x</a>");

        AssertStructurallySafe(result);
        Assert.Contains("<p>Hi <strong>there</strong></p>", result);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_returns_empty_when_nothing_permitted_remains()
    {
        // The Support handler relies on this to reject a reply with no permitted content.
        var result = SupportHtmlSanitizer.Sanitize(
            "<img src=x onerror=alert(1)><iframe src=\"javascript:alert(1)\"></iframe><svg onload=alert(1)></svg>");

        Assert.Equal(string.Empty, result);
    }


    public static TheoryData<string> IdempotencyInputs => new()
    {
        "Plain text reply.",
        "<p>Hello <strong>team</strong>, see <a href=\"https://example.com/help\">the docs</a>.</p>",
        "<a href=\"https://example.com/?a=1&b=2\">amp</a>",
        "<a href=\"mailto:support@example.com\">mail</a>",
        "<p>Hi <strong>there</strong></p><script>alert(1)</script><img src=x onerror=alert(1)><a href=\"javascript:alert(1)\">x</a>",
        "<a href=\"&#x20;javascript:alert(1)\">x</a>",
        "<a href=\"  https://example.com  \">padded</a>",
        "<scr<script>ipt>alert(1)</script>",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\"></noscript>",
        "<noscript><p>hi</p></noscript>",
        "<svg><style><img src=x onerror=alert(1)></style></svg>",
        "&lt;script&gt;alert(1)&lt;/script&gt;",
        "<pre><code>if (a < b) {}</code></pre>",
        "<b>bold <i>and italic <script>bad",
        "<p style=\"color:red\" class=\"x\" data-x=\"1\" onclick=\"y\" title=\"t\">mixed</p>",
        "<ul><li>one<li>two</ul><table><tr><td>cell</td></tr></table>",
        "<p>Tom &amp; Jerry &lt;3</p>",
    };

    [Theory]
    [MemberData(nameof(IdempotencyInputs))]
    public void Sanitize_is_idempotent(string input)
    {
        var once = SupportHtmlSanitizer.Sanitize(input);
        var twice = SupportHtmlSanitizer.Sanitize(once);

        Assert.Equal(once, twice);
        AssertStructurallySafe(once);
    }


    [Fact]
    public void Sanitize_is_safe_to_call_concurrently_and_always_yields_the_same_result()
    {
        const string input =
            "<p>Safe <strong>bold</strong> <a href=\"https://example.com/help\">help</a></p>" +
            "<script>window.__xss=1</script><img src=x onerror=\"window.__xss=2\"><a href=\"JaVaScRiPt:alert(1)\">x</a>";
        var expected = SupportHtmlSanitizer.Sanitize(input);
        var results = new ConcurrentBag<string>();

        Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 },
            _ => results.Add(SupportHtmlSanitizer.Sanitize(input)));

        Assert.Equal(200, results.Count);
        Assert.All(results, r => Assert.Equal(expected, r));
        AssertStructurallySafe(expected);
    }
}
