using System.Collections.Concurrent;
using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentBag<SentEmail> _sent = new();

    public IReadOnlyCollection<SentEmail> Sent => _sent;

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        _sent.Add(new SentEmail(toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }

    public sealed record SentEmail(string ToEmail, string Subject, string HtmlBody);
}
