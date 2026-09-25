using AngleSharp.Dom;
using Ganss.Xss;

namespace HR.SharedKernel.Html;

/// <summary>
/// The single allow-list sanitisation policy for support conversation bodies
/// (<c>support.support_responses.body_html</c>).
///
/// Support response bodies are authored by tenant users (support:manage) and are rendered as raw
/// markup in BOTH HR.Web (tenant support thread) and HR.Admin.Web (platform-admin support
/// console). Unsanitised, that is a stored XSS sink that crosses from a tenant origin into the
/// platform-admin origin. The same policy is therefore applied:
/// <list type="bullet">
/// <item>at write time, by the Support module, before every response body is persisted; and</item>
/// <item>at render time, by HR.Web and HR.Admin.Web, as defence in depth and for historical rows.</item>
/// </list>
///
/// Lives in HR.SharedKernel (same rationale as <see cref="FormText"/>) because the Support module,
/// HR.Web and HR.Admin.Web all already reference this project — there must be exactly one
/// allow-list, never a copy per consumer.
///
/// Everything not explicitly allowed is removed: no script/style/iframe/object/embed/svg/math/form
/// elements, no event-handler attributes, no inline styles, no class/id/data attributes, and only
/// <c>https:</c> and <c>mailto:</c> absolute URLs (so <c>javascript:</c>, <c>data:</c>,
/// <c>vbscript:</c> and <c>http:</c> are dropped, including entity-encoded, mixed-case and
/// whitespace-obfuscated spellings, because URLs are checked after HTML decoding and URI parsing).
///
/// The output is stable under re-sanitisation (<c>Sanitize(Sanitize(x)) == Sanitize(x)</c>), which
/// the stored-row backfill relies on to be idempotent.
/// </summary>
public static class SupportHtmlSanitizer
{
    /// <summary>Elements that survive sanitisation. Any other element is removed; its text content is kept.</summary>
    public static readonly IReadOnlySet<string> AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "b", "strong", "i", "em", "u", "s", "ul", "ol", "li",
        "blockquote", "code", "pre", "a", "span", "h3", "h4", "h5", "hr",
    };

    /// <summary>Attributes that survive sanitisation (in addition to the forced safe <c>rel</c>/<c>target</c> on links).</summary>
    public static readonly IReadOnlySet<string> AllowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "href", "title",
    };

    /// <summary>Absolute URL schemes permitted in <c>href</c>. Relative URLs are also permitted.</summary>
    public static readonly IReadOnlySet<string> AllowedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "https", "mailto",
    };

    /// <summary>The <c>rel</c> value forced onto every surviving link.</summary>
    public const string LinkRel = "noopener noreferrer nofollow";

    /// <summary>Disallowed elements whose content is discarded with them (never kept as text).</summary>
    private static readonly HashSet<string> DropContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "template", "noscript", "noembed", "noframes", "xmp", "plaintext",
        "iframe", "frame", "frameset", "object", "embed", "applet", "svg", "math",
        "title", "textarea", "select", "option",
    };

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();
    private static readonly Lock SanitizerLock = new();

    /// <summary>
    /// Returns an allow-listed copy of <paramref name="bodyHtml"/> that is safe to render as raw
    /// markup. Null, empty or whitespace-only input returns <see cref="string.Empty"/>.
    /// </summary>
    public static string Sanitize(string? bodyHtml)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml))
            return string.Empty;

        // HtmlSanitizer instances are not documented as thread-safe once PostProcessNode handlers
        // are attached; serialise access to the shared, pre-configured instance.
        lock (SanitizerLock)
        {
            return Sanitizer.Sanitize(bodyHtml).Trim();
        }
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var options = new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(AllowedTags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(AllowedAttributes, StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(AllowedSchemes, StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href" },
            AllowedCssProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>(),
            AllowedCssClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };

        var sanitizer = new HtmlSanitizer(options)
        {
            KeepChildNodes = true,
            AllowDataAttributes = false,
        };

        // KeepChildNodes preserves the visible text of harmless unlisted wrappers (div, table,
        // font, ...). For script/raw-text/embedded-content elements the "children" are code or
        // foreign markup, not reply text — drop them entirely instead of surfacing them as text.
        sanitizer.RemovingTag += (_, e) =>
        {
            if (DropContentTags.Contains(e.Tag.LocalName))
            {
                while (e.Tag.FirstChild is { } child)
                    child.RemoveFromParent();
            }
        };

        // Force a safe rel/target on every surviving link. These are (re)applied after attribute
        // filtering on every pass, which keeps re-sanitisation output stable.
        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is IElement { LocalName: "a" } element && element.HasAttribute("href"))
            {
                element.SetAttribute("rel", LinkRel);
                element.SetAttribute("target", "_blank");
            }
        };

        return sanitizer;
    }
}
