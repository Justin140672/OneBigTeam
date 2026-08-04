using System.Collections.Concurrent;
using HR.SharedKernel;

namespace HR.Modules.Support.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IEmailSender"/>. Set <see cref="ThrowOnSend"/> to simulate an email
/// provider outage so handlers can be verified to still record a notification attempt (marked
/// Failed) rather than letting the exception bubble up and abort the whole request.
/// </summary>
internal sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentBag<SentEmail> _sent = new();

    public bool ThrowOnSend { get; set; }

    public IReadOnlyCollection<SentEmail> Sent => _sent;

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (ThrowOnSend)
            throw new InvalidOperationException("Simulated email provider failure.");

        _sent.Add(new SentEmail(toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }

    public sealed record SentEmail(string ToEmail, string Subject, string HtmlBody);
}
