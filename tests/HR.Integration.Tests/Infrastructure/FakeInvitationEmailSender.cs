using HR.SharedKernel;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IInvitationEmailSender"/> (the branded-template invitation path
/// introduced by the Postmark user-invitation integration). Records the send into the shared
/// <see cref="FakeEmailSender"/> so existing assertions over <c>_factory.EmailSender.Sent</c>
/// continue to work without every invitation test needing a second capture surface.
/// </summary>
public sealed class FakeInvitationEmailSender(FakeEmailSender emailSender) : IInvitationEmailSender
{
    // Mirrors the subject the pre-template inline invitation email used, which the integration
    // tests assert on.
    public const string Subject = "You have been invited to One Big Team";

    public Task<bool> SendAsync(
        string toEmail,
        string? recipientName,
        string actionUrl,
        CancellationToken ct = default)
    {
        var html = $"<p>You have been invited. <a href=\"{actionUrl}\">Accept your invite</a></p>";
        emailSender.SendAsync(toEmail, Subject, html, ct);
        return Task.FromResult(true);
    }
}
