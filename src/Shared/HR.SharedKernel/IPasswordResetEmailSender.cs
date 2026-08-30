namespace HR.SharedKernel;

/// <summary>
/// Sends password-reset emails via a branded template (e.g. the Postmark <c>password-reset</c>
/// template). Mirrors <see cref="IInvitationEmailSender"/>: the interface carries the
/// reset-specific template parameters rather than a raw HTML body.
/// </summary>
public interface IPasswordResetEmailSender
{
    /// <summary>
    /// Sends (or attempts to send) a password-reset email. <paramref name="actionUrl"/> must be the
    /// real Supabase-generated recovery link. <paramref name="userAgent"/> is the raw browser
    /// User-Agent captured when the reset was requested — used only to render friendly
    /// "browser_name"/"operating_system" values in the email body; it is not a security control.
    /// Returns <c>true</c> when the message was accepted for delivery. Implementations must not throw.
    /// </summary>
    Task<bool> SendAsync(
        string toEmail,
        string? recipientName,
        string actionUrl,
        string? userAgent,
        CancellationToken ct = default);
}
