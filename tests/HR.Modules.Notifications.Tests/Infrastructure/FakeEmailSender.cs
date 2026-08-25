using HR.SharedKernel;

namespace HR.Modules.Notifications.Tests.Infrastructure;

/// <summary>
/// Fake Postmark/email sender for NOT-02 tests. No fake IEmailSender previously existed in this
/// codebase (only PostmarkEmailSender and LoggingEmailSender in HR.Infrastructure/Email — both real
/// implementations). Configurable to succeed, fail transiently (throw for a bounded number of calls
/// then succeed), or fail permanently (always throw); records every call for assertion.
/// </summary>
internal sealed class FakeEmailSender : IEmailSender
{
    public sealed record Call(string ToEmail, string Subject, string HtmlBody);

    private readonly int _failuresBeforeSuccess;
    private readonly Func<Exception> _exceptionFactory;
    private int _callCount;

    public List<Call> Calls { get; } = [];

    /// <param name="failuresBeforeSuccess">
    /// Number of calls that throw before a call finally succeeds. Use <c>int.MaxValue</c> for
    /// "always fails" (permanent failure). Use 0 for "always succeeds".
    /// </param>
    /// <param name="exceptionFactory">Factory for the exception thrown on a failing call; defaults to HttpRequestException.</param>
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
