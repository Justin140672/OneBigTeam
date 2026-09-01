using Ganss.Xss;

namespace HR.Web.Services;

/// <summary>
/// Allow-list sanitiser for support conversation bodies.
///
/// Support reply/response content (<c>BodyHtml</c>) is authored by support staff AND by end-user
/// submitters, round-trips through the API, and is rendered raw via <see cref="Microsoft.AspNetCore.Components.MarkupString"/>
/// on the support thread page. Without sanitisation that is a stored XSS sink (CodeQL alert #9).
///
/// Everything not on the explicit allow-list below is stripped: no <c>script</c>/<c>style</c>/<c>iframe</c>,
/// no event handler attributes (<c>onerror</c> etc.), no <c>javascript:</c>/<c>data:</c> URLs, no inline styles.
/// </summary>
public static class SupportHtmlSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static string Sanitize(string? bodyHtml)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml))
            return string.Empty;

        lock (Sanitizer)
        {
            return Sanitizer.Sanitize(bodyHtml);
        }
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 {
                     "p", "br", "b", "strong", "i", "em", "u", "s", "ul", "ol", "li",
                     "blockquote", "code", "pre", "a", "span", "h3", "h4", "h5", "hr",
                 })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("title");

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");

        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();
        sanitizer.KeepChildNodes = true;

        // Force safe rel on any surviving links.
        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement { LocalName: "a" } element && element.HasAttribute("href"))
            {
                element.SetAttribute("rel", "noopener noreferrer nofollow");
                element.SetAttribute("target", "_blank");
            }
        };

        return sanitizer;
    }
}
