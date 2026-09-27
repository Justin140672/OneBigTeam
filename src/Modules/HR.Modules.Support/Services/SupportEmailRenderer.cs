using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using HR.Modules.Support.Domain;

namespace HR.Modules.Support.Services;

/// <summary>A rendered support notification email: a header-safe subject and an HTML body.</summary>
internal sealed record SupportEmail(string Subject, string HtmlBody);

/// <summary>
/// The single rendering point for every email the Support module sends (new-request admin alert,
/// staff-reply customer notification and the retry-job notification), so all paths share one
/// encoding policy and cannot drift.
/// <para>
/// Policy: every dynamic value interpolated into an HTML body goes through <see cref="Encode"/> —
/// support request titles are plain text, never rich text, so they are always encoded and never
/// sanitised. The encoder escapes all HTML-significant characters (&lt; &gt; &amp; " ' and more)
/// while leaving letters from every Unicode range literal so non-English titles stay readable.
/// Every subject goes through <see cref="SanitizeSubject"/>, which removes CR/LF and other
/// control/line-separator characters (header injection) and bounds the length.
/// </para>
/// </summary>
internal static class SupportEmailRenderer
{
    internal const int MaxSubjectLength = 200;

    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    public static SupportEmail RenderNewRequestAdminAlert(
        string referenceNumber,
        SupportRequestType type,
        SupportRequestPriority priority,
        string title,
        string? viewRequestLink)
    {
        // viewRequestLink must already be a validated absolute http(s) URI built from trusted
        // configuration (see SubmitSupportRequestHandler); it is still attribute-encoded here.
        var linkHtml = viewRequestLink is null
            ? string.Empty
            : $"""
              <p style="margin:24px 0">
                <a href="{Encode(viewRequestLink)}" style="background:#0d6efd;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
                  View Request
                </a>
              </p>
              """;

        var body = $"""
            <html>
            <body style="font-family:sans-serif;max-width:600px;margin:auto;padding:24px">
              <h1>New Support Request</h1>
              <p><strong>Reference:</strong> {Encode(referenceNumber)}</p>
              <p><strong>Type:</strong> {Encode(type.ToString())}</p>
              <p><strong>Priority:</strong> {Encode(priority.ToString())}</p>
              <p><strong>Title:</strong> {Encode(title)}</p>
              {linkHtml}
            </body>
            </html>
            """;

        return new SupportEmail(SanitizeSubject($"New support request: {referenceNumber}"), body);
    }

    public static SupportEmail RenderStaffReplyCustomerNotification(string referenceNumber, string title)
    {
        var body =
            $"<p>There's a new reply on your support request <strong>{Encode(referenceNumber)}</strong> — \"{Encode(title)}\".</p>" +
            "<p>Sign in to view the full conversation and respond.</p>";

        return new SupportEmail(SanitizeSubject($"Update on your support request {referenceNumber}"), body);
    }

    public static SupportEmail RenderRetryNotification(string referenceNumber)
    {
        var body = $"<p>This is a retried notification for support request {Encode(referenceNumber)}.</p>";

        return new SupportEmail(SanitizeSubject($"[Retry] Support request update: {referenceNumber}"), body);
    }

    /// <summary>HTML-encodes a plain-text value for element-text or quoted-attribute contexts.</summary>
    public static string Encode(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : Encoder.Encode(value);

    /// <summary>
    /// Makes a value safe to use as an email subject header: every control character (including
    /// CR, LF, TAB, NUL, NEL) and the Unicode line/paragraph separators are replaced with a space,
    /// runs of whitespace are collapsed, and the result is trimmed and bounded to
    /// <see cref="MaxSubjectLength"/> characters without splitting a surrogate pair.
    /// </summary>
    public static string SanitizeSubject(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;

        foreach (var c in value)
        {
            var isUnsafe = char.IsControl(c) || c is (char)0x2028 or (char)0x2029;
            var isSpace = isUnsafe || char.IsWhiteSpace(c);

            if (isSpace)
            {
                if (!previousWasSpace)
                    builder.Append(' ');
                previousWasSpace = true;
                continue;
            }

            builder.Append(c);
            previousWasSpace = false;
        }

        var subject = builder.ToString().Trim();
        if (subject.Length <= MaxSubjectLength)
            return subject;

        var cut = MaxSubjectLength;
        if (char.IsHighSurrogate(subject[cut - 1]))
            cut--;

        return subject[..cut].TrimEnd();
    }
}
