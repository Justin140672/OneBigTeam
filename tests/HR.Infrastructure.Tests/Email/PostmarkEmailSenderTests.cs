using System.Net;
using HR.Infrastructure.Email;
using HR.Infrastructure.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Tests.Email;

/// <summary>
/// TEST-004 — Postmark adapter hardening. A permanent rejection (non-2xx, or ErrorCode != 0)
/// must surface as a terminal <see cref="HttpRequestException"/> carrying a 4xx status; a transient
/// fault (5xx / timeout / transport error) must surface in a way a retry policy can distinguish
/// (5xx status on the exception, or a raw transport / cancellation exception). No secret token or
/// email body may appear in logs.
/// </summary>
public class PostmarkEmailSenderTests
{
    private static PostmarkEmailSender Build(FakeHttpMessageHandler handler, Microsoft.Extensions.Logging.ILogger<PostmarkEmailSender>? logger = null)
    {
        var http = new HttpClient(handler);
        var options = Options.Create(new PostmarkOptions
        {
            ServerToken = "super-secret-server-token",
            FromEmail = "no-reply@example.com",
            MessageStream = "outbound",
        });
        return new PostmarkEmailSender(http, options, logger ?? NullLogger<PostmarkEmailSender>.Instance);
    }

    private const string TokenBody = "<a href=\"https://app/reset?token=pkce_secret_9f8e7d6c\">Reset</a>";

    [Fact]
    public async Task SendAsync_Success_Posts_To_Postmark_Email_Endpoint_With_Payload()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = """{"ErrorCode":0,"Message":"OK"}""",
        };
        var sender = Build(handler);

        await sender.SendAsync("ada@example.com", "Welcome", "<p>hi</p>");

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.postmarkapp.com/email", request.RequestUri!.ToString());
        Assert.Contains("ada@example.com", body);
        Assert.Contains("no-reply@example.com", body);
        Assert.Contains("outbound", body);
        Assert.Equal("super-secret-server-token", request.Headers.GetValues("X-Postmark-Server-Token").Single());
    }

    // ---- terminal failures --------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)] // 422 — Postmark's inactive-recipient / bad-request shape
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task SendAsync_4xx_Rejection_Is_Terminal_With_Client_Status(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = status,
            ResponseBodyToReturn = """{"ErrorCode":406,"Message":"Inactive recipient"}""",
        };
        var sender = Build(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => sender.SendAsync("ada@example.com", "Welcome", "<p>hi</p>"));

        Assert.Equal(status, ex.StatusCode);
        Assert.True((int)ex.StatusCode! is >= 400 and < 500, "4xx must be treated as terminal, not retried");
    }

    // ---- transient failures -----------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SendAsync_5xx_Is_Retryable_With_Server_Status(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = status,
            ResponseBodyToReturn = "upstream boom",
        };
        var sender = Build(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => sender.SendAsync("ada@example.com", "Welcome", "<p>hi</p>"));

        Assert.Equal(status, ex.StatusCode);
        Assert.True((int)ex.StatusCode! >= 500, "5xx must be distinguishable as retryable");
    }

    [Fact]
    public async Task SendAsync_Transport_Failure_Propagates_As_Retryable_HttpRequestException()
    {
        var handler = new FakeHttpMessageHandler
        {
            ExceptionToThrow = new HttpRequestException("name resolution failed"),
        };
        var sender = Build(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => sender.SendAsync("ada@example.com", "Welcome", "<p>hi</p>"));

        Assert.Null(ex.StatusCode); // no HTTP response at all — a transport-level, retryable fault
    }

    [Fact]
    public async Task SendAsync_Honours_Cancellation_Token()
    {
        var handler = new FakeHttpMessageHandler { Delay = TimeSpan.FromSeconds(30) };
        var sender = Build(handler);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync("ada@example.com", "Welcome", "<p>hi</p>", cts.Token));
    }

    [Fact]
    public async Task SendAsync_Malformed_Error_Body_Still_Fails_Terminally_Not_Crashes()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.UnprocessableEntity,
            ResponseBodyToReturn = "<html>not json</html>",
        };
        var sender = Build(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => sender.SendAsync("ada@example.com", "Welcome", "<p>hi</p>"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
    }

    // ---- sensitive value scrubbing --------------------------------------------------

    [Fact]
    public async Task SendAsync_Failure_Does_Not_Log_Server_Token_Email_Or_Body()
    {
        var logger = new ListLogger<PostmarkEmailSender>();
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.UnprocessableEntity,
            ResponseBodyToReturn =
                """{"ErrorCode":406,"Message":"Inactive recipient","leaked":"eyJhbGciOiJIUzI1NiJ9.leak.sig"}""",
        };
        var sender = Build(handler, logger);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => sender.SendAsync("ada@example.com", "Welcome", TokenBody));

        Assert.DoesNotContain("super-secret-server-token", logger.Text);
        Assert.DoesNotContain("pkce_secret_9f8e7d6c", logger.Text);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", logger.Text);
        Assert.DoesNotContain("ada@example.com", logger.Text);
        // still useful for diagnosis
        Assert.Contains("406", logger.Text);
        Assert.Contains("Inactive recipient", logger.Text);
    }

    private sealed class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public string Text => string.Join("\n", Messages);
        IDisposable? Microsoft.Extensions.Logging.ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = formatter(state, exception);
            if (exception is not null) line += " | " + exception;
            Messages.Add(line);
        }
    }
}
