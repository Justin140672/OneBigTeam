using System.Reflection;
using System.Text.RegularExpressions;
using Markdig;

namespace HR.Marketing.Services;

/// <summary>An H2 heading extracted from a rendered document, used to build a simple table of contents.</summary>
public sealed record MarkdownHeading(string Id, string Text);

/// <summary>Parsed result of a Documents/*.md file: its front-matter metadata plus rendered HTML body.</summary>
public sealed record MarkdownDocument(string Title, string LastUpdated, string Html, IReadOnlyList<MarkdownHeading> Headings);

/// <summary>
/// Loads the legal/product markdown files embedded from Documents/*.md (see HR.Marketing.csproj)
/// and renders them to HTML. Each file starts with a small "title" / "lastUpdated" front-matter
/// block (not full YAML — just two known keys) followed by the markdown body.
/// </summary>
public static class MarkdownDocumentLoader
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoLinks() // ensures bare email addresses (e.g. security@onebigteam.co.uk) render as clickable mailto: links
        .Build();

    /// <summary>Loads Documents/{slug}.md, e.g. slug "privacy-policy" for Documents/privacy-policy.md.</summary>
    public static MarkdownDocument Load(string slug)
    {
        var resourceName = $"HR.Marketing.Documents.{slug}.md";
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded document '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var raw = reader.ReadToEnd();

        var title = slug;
        var lastUpdated = string.Empty;
        var body = raw;

        if (raw.StartsWith("---", StringComparison.Ordinal))
        {
            var endIndex = raw.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (endIndex > 0)
            {
                var frontMatter = raw[3..endIndex];
                body = raw[(endIndex + 4)..].TrimStart('\r', '\n');

                foreach (var line in frontMatter.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (key.Equals("title", StringComparison.OrdinalIgnoreCase))
                    {
                        title = value;
                    }
                    else if (key.Equals("lastUpdated", StringComparison.OrdinalIgnoreCase))
                    {
                        lastUpdated = value;
                    }
                }
            }
        }

        // The body's own leading "# Title" heading duplicates the page's <h1> (rendered
        // separately from the front-matter title above it), so drop it before rendering.
        body = body.TrimStart('\r', '\n');
        if (body.StartsWith("# ", StringComparison.Ordinal))
        {
            var newlineIndex = body.IndexOf('\n');
            body = newlineIndex >= 0 ? body[(newlineIndex + 1)..].TrimStart('\r', '\n') : string.Empty;
        }

        var html = Markdown.ToHtml(body, Pipeline);
        var headings = ExtractH2Headings(html);
        return new MarkdownDocument(title, lastUpdated, html, headings);
    }

    // Markdig's advanced-extensions bundle auto-generates a slug "id" attribute on every heading
    // (the auto-identifiers extension), so H2s can be turned into a simple anchor-link table of
    // contents for longer documents without any extra authoring in the .md files themselves.
    private static readonly Regex H2Pattern = new("""<h2 id="([^"]+)">(.*?)</h2>""", RegexOptions.Compiled | RegexOptions.Singleline);

    private static IReadOnlyList<MarkdownHeading> ExtractH2Headings(string html) =>
        H2Pattern.Matches(html)
            .Select(m => new MarkdownHeading(m.Groups[1].Value, Regex.Replace(m.Groups[2].Value, "<.*?>", string.Empty)))
            .ToList();
}
