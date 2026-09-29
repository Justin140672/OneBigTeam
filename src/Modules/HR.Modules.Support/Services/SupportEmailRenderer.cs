using HR.SharedKernel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using HR.Modules.Support.Domain;

namespace HR.Modules.Support.Services;

internal sealed record SupportEmail(string Subject, string HtmlBody);

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
              <p><strong>Type:</strong> {Encode(EnumText.Humanize(type))}</p>
              <p><strong>Priority:</strong> {Encode(EnumText.Humanize(priority))}</p>
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

    public static string Encode(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : Encoder.Encode(value);

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
