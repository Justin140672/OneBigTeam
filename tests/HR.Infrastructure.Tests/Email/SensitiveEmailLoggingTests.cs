using System.Net;
using HR.Infrastructure.Email;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Tests.Email;

/// <summary>
/// Ticket 2 — the stub ("logging") email senders and the Postmark sender must stay useful for
/// diagnosing delivery success/failure without ever writing invitation tokens, recovery links,
/// email bodies or Postmark API payloads to the log.
/// </summary>
public class SensitiveEmailLoggingTests
{
    private const string InviteUrlWithToken =
        "https://app.example.com/accept-invite?token=inv_9f8e7d6c5b4a3210deadbeef";
    private const string RecoveryUrlWithToken =
        "https://proj.supabase.co/auth/v1/verify?token=pkce_1122334455&type=recovery";

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public string Text => string.Join("\n", Messages);
        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var line = formatter(state, exception);
            if (exception is not null) line += " | " + exception;
            Messages.Add(line);
        }
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task LoggingInvitationEmailSender_Does_Not_Log_Action_Url_Or_Token()
    {
        var logger = new ListLogger<LoggingInvitationEmailSender>();
        var sender = new LoggingInvitationEmailSender(logger);

        var sent = await sender.SendAsync("new.hire@example.com", "New Hire", InviteUrlWithToken);

        Assert.True(sent);
        Assert.DoesNotContain(InviteUrlWithToken, logger.Text);
        Assert.DoesNotContain("inv_9f8e7d6c5b4a3210deadbeef", logger.Text);
        Assert.DoesNotContain("new.hire@example.com", logger.Text);
        // still useful: delivery attempt is recorded
        Assert.Contains("INVITATION EMAIL (stub)", logger.Text);
    }

    [Fact]
    public async Task LoggingPasswordResetEmailSender_Does_Not_Log_Action_Url_Or_Token()
    {
        var logger = new ListLogger<LoggingPasswordResetEmailSender>();
        var sender = new LoggingPasswordResetEmailSender(logger);

        var sent = await sender.SendAsync("ada@example.com", "Ada", RecoveryUrlWithToken, "Mozilla/5.0");

        Assert.True(sent);
        Assert.DoesNotContain(RecoveryUrlWithToken, logger.Text);
        Assert.DoesNotContain("pkce_1122334455", logger.Text);
        Assert.DoesNotContain("ada@example.com", logger.Text);
        Assert.Contains("PASSWORD RESET EMAIL (stub)", logger.Text);
    }

    [Fact]
    public async Task LoggingEmailSender_Does_Not_Log_Html_Body()
    {
        var logger = new ListLogger<LoggingEmailSender>();
        var sender = new LoggingEmailSender(logger);

        var html = $"<a href=\"{RecoveryUrlWithToken}\">Reset your password</a>";
        await sender.SendAsync("ada@example.com", "Reset your password", html);

        Assert.DoesNotContain(RecoveryUrlWithToken, logger.Text);
        Assert.DoesNotContain("pkce_1122334455", logger.Text);
        Assert.DoesNotContain("ada@example.com", logger.Text);
        Assert.Contains("Reset your password", logger.Text); // subject is safe to log
    }

    [Fact]
    public async Task PostmarkEmailSender_Failure_Log_Excludes_Response_Body_And_Tokens()
    {
        var logger = new ListLogger<PostmarkEmailSender>();
        var responseBody =
            """
            {
                "ErrorCode": 406,
                "Message": "Inactive recipient",
                "leaked_token": "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.leaked-signature-value"
            }
            """;
        var http = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.UnprocessableEntity, responseBody));
        var options = Options.Create(new PostmarkOptions { ServerToken = "tok", FromEmail = "no-reply@example.com" });
        var sender = new PostmarkEmailSender(http, options, logger);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => sender.SendAsync("ada@example.com", "Welcome", "<a href=\"https://x/secret?token=abc\">link</a>"));

        Assert.DoesNotContain("leaked-signature-value", logger.Text);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", logger.Text);
        Assert.DoesNotContain("leaked_token", logger.Text);
        Assert.DoesNotContain("ada@example.com", logger.Text);
        // still useful for diagnosis: Postmark's own error code/message survive
        Assert.Contains("406", logger.Text);
        Assert.Contains("Inactive recipient", logger.Text);
    }
}
