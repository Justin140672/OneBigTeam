using System.Net;
using System.Text.RegularExpressions;
using HR.SharedKernel;

namespace HR.Modules.Notifications.Domain;

/// <summary>Deterministic rendering output for a single template + token dictionary pair.</summary>
internal sealed record RenderedNotification(
    string InAppTitle,
    string? InAppBody,
    string EmailSubject,
    string EmailBody);

/// <summary>
/// NOT-03: simple, deterministic "{TokenName}" substitution engine — deliberately not a full
/// templating engine (no dependency such as Scriban/Handlebars exists elsewhere in this codebase),
/// which keeps rendering easy to reason about and test.
///
/// Two distinct validations are performed, at two different times:
///  - RequiredTokensMissing (checked on every call to Render, i.e. at render/write time): the
///    caller-supplied token dictionary must contain every token the template declares as required.
///    This is what lets NotificationWriter.WriteTemplatedAsync fail before anything is queued.
///  - FindUndeclaredTokenPlaceholders (checked only by NotificationTemplateCatalogueTests, walking
///    the whole catalogue): every "{Token}" placeholder actually used inside a template string must
///    be declared in that same template's RequiredTokens/OptionalTokens. This is a template-authoring
///    consistency check — it catches a typo'd placeholder in a shipped template before it reaches
///    production, not a per-request runtime concern.
///
/// HTML encoding: only email body token values are HTML-encoded before substitution (the email body
/// template is HTML markup, so an unescaped value could break markup or inject content). In-app
/// title/body are plain text surfaces (rendered as-is in the Blazor notification list), so their
/// token values are substituted without encoding. Email subject is a plain email header value (not
/// HTML), so it is also substituted without HTML-encoding.
/// </summary>
internal static class NotificationTemplateRenderer
{
    private static readonly Regex TokenPattern = new(@"\{(\w+)\}", RegexOptions.Compiled);

    public static Result<RenderedNotification> Render(
        NotificationTemplate template,
        IReadOnlyDictionary<string, string> tokens)
    {
        var missing = template.RequiredTokens
            .Where(required => !tokens.ContainsKey(required))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        if (missing.Count > 0)
        {
            return Result.Failure<RenderedNotification>(Error.Validation(
                $"Missing required notification template token(s): {string.Join(", ", missing)}."));
        }

        var inAppTitle = Substitute(template.InAppTitleTemplate, tokens, htmlEncodeValues: false);
        var inAppBody = template.InAppBodyTemplate is null
            ? null
            : NullIfEmpty(Substitute(template.InAppBodyTemplate, tokens, htmlEncodeValues: false));
        var emailSubject = Substitute(template.EmailSubjectTemplate, tokens, htmlEncodeValues: false);
        var emailBody = Substitute(template.EmailBodyTemplate, tokens, htmlEncodeValues: true);

        return Result.Success(new RenderedNotification(inAppTitle, inAppBody, emailSubject, emailBody));
    }

    /// <summary>
    /// Returns every "{Token}" placeholder found across the template's four strings that is not
    /// present in the union of RequiredTokens/OptionalTokens — used only by architecture-style
    /// catalogue tests, not at render time.
    /// </summary>
    public static IReadOnlyList<string> FindUndeclaredTokenPlaceholders(NotificationTemplate template)
    {
        var declared = template.RequiredTokens.Union(template.OptionalTokens).ToHashSet(StringComparer.Ordinal);

        var used = new[]
            {
                template.InAppTitleTemplate,
                template.InAppBodyTemplate ?? string.Empty,
                template.EmailSubjectTemplate,
                template.EmailBodyTemplate,
            }
            .SelectMany(s => TokenPattern.Matches(s).Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal);

        return used.Where(token => !declared.Contains(token)).OrderBy(t => t, StringComparer.Ordinal).ToList();
    }

    private static string Substitute(
        string templateText,
        IReadOnlyDictionary<string, string> tokens,
        bool htmlEncodeValues)
    {
        return TokenPattern.Replace(templateText, match =>
        {
            var tokenName = match.Groups[1].Value;
            if (!tokens.TryGetValue(tokenName, out var value))
                return string.Empty;

            return htmlEncodeValues ? WebUtility.HtmlEncode(value) : value;
        });
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
