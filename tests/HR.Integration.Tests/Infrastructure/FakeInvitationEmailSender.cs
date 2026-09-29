using HR.SharedKernel;

namespace HR.Integration.Tests.Infrastructure;

public sealed class FakeInvitationEmailSender(FakeEmailSender emailSender) : IInvitationEmailSender
{
    public const string Subject = "You have been invited to One Big Team";

    private readonly object _lock = new();
    private readonly HashSet<string> _failingEmails = new(StringComparer.OrdinalIgnoreCase);

    public void FailFor(string email)
    {
        lock (_lock) { _failingEmails.Add(email); }
    }

    public void Reset()
    {
        lock (_lock) { _failingEmails.Clear(); }
    }

    public Task<bool> SendAsync(
        string toEmail,
        string? recipientName,
        string actionUrl,
        CancellationToken ct = default)
    {
        var html = $"<p>You have been invited. <a href=\"{actionUrl}\">Accept your invite</a></p>";
        emailSender.SendAsync(toEmail, Subject, html, ct);

        bool shouldFail;
        lock (_lock) { shouldFail = _failingEmails.Contains(toEmail); }

        return Task.FromResult(!shouldFail);
    }
}
