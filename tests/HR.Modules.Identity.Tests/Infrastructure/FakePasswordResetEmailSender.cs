using System.Collections.Concurrent;
using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakePasswordResetEmailSender : IPasswordResetEmailSender
{
    private readonly ConcurrentBag<SentReset> _sent = new();
    private readonly bool _succeeds;

    public FakePasswordResetEmailSender(bool succeeds = true) => _succeeds = succeeds;

    public IReadOnlyCollection<SentReset> Sent => _sent;

    public Task<bool> SendAsync(
        string toEmail,
        string? recipientName,
        string actionUrl,
        string? userAgent,
        CancellationToken ct = default)
    {
        _sent.Add(new SentReset(toEmail, recipientName, actionUrl, userAgent));
        return Task.FromResult(_succeeds);
    }

    public sealed record SentReset(string ToEmail, string? RecipientName, string ActionUrl, string? UserAgent);
}
