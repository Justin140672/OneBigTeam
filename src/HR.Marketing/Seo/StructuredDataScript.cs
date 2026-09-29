using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HR.Marketing.Seo;

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
