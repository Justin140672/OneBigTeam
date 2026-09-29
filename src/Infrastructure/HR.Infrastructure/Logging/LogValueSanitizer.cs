using System.Text;

namespace HR.Infrastructure.Logging;

public static class LogValueSanitizer
{
    private const int MaxLength = 2048;
    private const char Replacement = '\uFFFD';

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
