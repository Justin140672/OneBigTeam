using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HR.Marketing.Seo;

/// <summary>
/// Builds the JSON-LD payload emitted inside
/// <c>&lt;script type="application/ld+json"&gt;...&lt;/script&gt;</c> by <c>SeoHead</c>.
///
/// The previous implementation string-replaced <c>__SITE_URL__</c> / <c>__CANONICAL_URL__</c> tokens
/// in a raw JSON blob and dumped the result through <c>MarkupString</c> (CodeQL alert #10 — the sink is
/// unsafe regardless of current call sites). This type closes that:
///
/// 1. Token values are JSON-string-escaped before substitution.
/// 2. The whole payload is strictly parsed as JSON; anything that is not well-formed JSON
///    (e.g. a raw <c>&lt;/script&gt;</c> breakout, <c>&lt;!--</c>, <c>]]&gt;</c>) yields <see langword="null"/>
///    and nothing is rendered.
/// 3. It is re-serialised with <see cref="JavaScriptEncoder.Default"/>, which escapes
///    <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c> and <c>+</c> as <c>\uXXXX</c> — making a
///    <c>&lt;/script&gt;</c> / comment / CDATA breakout impossible even for a valid JSON string value.
/// </summary>
public static class StructuredDataScript
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.Default,
        WriteIndented = false,
    };

    public static string? Build(string? rawJson, IReadOnlyDictionary<string, string>? tokenReplacements = null)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return null;

        var working = rawJson;
        if (tokenReplacements is { Count: > 0 })
        {
            foreach (var (token, value) in tokenReplacements)
            {
                // Escape for a JSON string context so a hostile replacement value cannot break the JSON
                // structure. JsonEncodedText.Encode also escapes '<' '>' '&' etc.
                var encoded = JsonEncodedText.Encode(value, JavaScriptEncoder.Default).ToString();
                working = working.Replace(token, encoded, StringComparison.Ordinal);
            }
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(working);
        }
        catch (JsonException)
        {
            return null;
        }

        return node?.ToJsonString(SerializerOptions);
    }
}
