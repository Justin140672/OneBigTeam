namespace HR.SharedKernel;

/// <summary>
/// Sends employee invitation emails, potentially via a branded template.
/// Implementations may use an email-service template (e.g. Postmark user-invitation
/// template) rather than inline HTML, so the interface carries invitation-specific
/// parameters rather than a raw HTML body.
/// </summary>
public interface IInvitationEmailSender
{
    /// <summary>
    /// Sends (or attempts to send) an invitation email.
    /// Returns <c>true</c> when the message was accepted for delivery;
    /// <c>false</c> when the underlying provider declined or was unavailable.
    /// Implementations must not throw — failures are logged internally.
    /// </summary>
    Task<bool> SendAsync(
        string toEmail,
        string? recipientName,
        string actionUrl,
        CancellationToken ct = default);
}
