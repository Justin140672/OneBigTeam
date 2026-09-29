using HR.SharedKernel;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeEmailSender : IEmailSender
{
    public sealed record Call(string ToEmail, string Subject, string HtmlBody);

    private readonly int _failuresBeforeSuccess;
    private readonly Func<Exception> _exceptionFactory;
    private int _callCount;

    public List<Call> Calls { get; } = [];

    public FakeEmailSender(int failuresBeforeSuccess = 0, Func<Exception>? exceptionFactory = null)
    {
        _failuresBeforeSuccess = failuresBeforeSuccess;
        _exceptionFactory = exceptionFactory ?? (() => new HttpRequestException("Simulated Postmark failure."));
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        _callCount++;
        if (_callCount <= _failuresBeforeSuccess)
        {
            throw _exceptionFactory();
        }

        Calls.Add(new Call(toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }
}
