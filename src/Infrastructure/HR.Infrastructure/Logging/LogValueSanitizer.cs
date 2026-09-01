using System.Text;

namespace HR.Infrastructure.Logging;

/// <summary>
/// Neutralises user-controlled values before they are attached to a log event so that
/// CR/LF and other control characters cannot forge or mislead log entries
/// ("log injection" / CWE-117). Values are also length-capped to bound log volume.
/// </summary>
public static class LogValueSanitizer
{
    private const int MaxLength = 2048;
    private const char Replacement = '\uFFFD';

    /// <summary>
    /// Returns a single-line, control-character-free representation of <paramref name="value"/>.
    /// CR / LF / tab and every other control character (plus the Unicode line/paragraph
    /// separators) are rewritten so a forged newline can never appear in rendered log output.
    /// </summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var truncated = value.Length > MaxLength;
        var source = truncated ? value.AsSpan(0, MaxLength) : value.AsSpan();

        var builder = new StringBuilder(source.Length + 8);
        foreach (var c in source)
        {
            if (c == '\u2028' || c == '\u2029' || char.IsControl(c))
            {
                builder.Append(Replacement);
            }
            else
            {
                builder.Append(c);
            }
        }

        if (truncated)
        {
            builder.Append("...");
        }

        return builder.ToString();
    }
}
