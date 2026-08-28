using System.Collections.Concurrent;
using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeInvitationEmailSender : IInvitationEmailSender
{
    private readonly ConcurrentBag<SentInvitation> _sent = new();
    private readonly bool _succeeds;

    public FakeInvitationEmailSender(bool succeeds = true) => _succeeds = succeeds;

    public IReadOnlyCollection<SentInvitation> Sent => _sent;

    public Task<bool> SendAsync(
        string toEmail,
        string? recipientName,
        string actionUrl,
        CancellationToken ct = default)
    {
        _sent.Add(new SentInvitation(toEmail, recipientName, actionUrl));
        return Task.FromResult(_succeeds);
    }

    public sealed record SentInvitation(string ToEmail, string? RecipientName, string ActionUrl);
}
