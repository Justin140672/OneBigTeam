using AngleSharp.Dom;
using Ganss.Xss;

namespace HR.SharedKernel.Html;

public static class SupportHtmlSanitizer
{
    public static readonly IReadOnlySet<string> AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "b", "strong", "i", "em", "u", "s", "ul", "ol", "li",
        "blockquote", "code", "pre", "a", "span", "h3", "h4", "h5", "hr",
    };

    public static readonly IReadOnlySet<string> AllowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "href", "title",
    };

    public static readonly IReadOnlySet<string> AllowedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "https", "mailto",
    };

    public const string LinkRel = "noopener noreferrer nofollow";

    private static readonly HashSet<string> DropContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "template", "noscript", "noembed", "noframes", "xmp", "plaintext",
        "iframe", "frame", "frameset", "object", "embed", "applet", "svg", "math",
        "title", "textarea", "select", "option",
    };

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();
    private static readonly Lock SanitizerLock = new();

    public static string Sanitize(string? bodyHtml)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml))
            return string.Empty;

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

        sanitizer.RemovingTag += (_, e) =>
        {
            if (DropContentTags.Contains(e.Tag.LocalName))
            {
                while (e.Tag.FirstChild is { } child)
                    child.RemoveFromParent();
            }
        };

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
