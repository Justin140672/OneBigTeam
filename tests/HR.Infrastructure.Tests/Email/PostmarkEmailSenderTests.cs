using System.Net;
using HR.Infrastructure.Email;
using HR.Infrastructure.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
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

        await sender.SendAsync("ada@customer-mail.co", "Welcome", "<p>hi</p>");

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.postmarkapp.com/email", request.RequestUri!.ToString());
        Assert.Contains("ada@customer-mail.co", body);
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
            () => sender.SendAsync("ada@customer-mail.co", "Welcome", "<p>hi</p>"));

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
            () => sender.SendAsync("ada@customer-mail.co", "Welcome", "<p>hi</p>"));

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
            () => sender.SendAsync("ada@customer-mail.co", "Welcome", "<p>hi</p>"));

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
            () => sender.SendAsync("ada@customer-mail.co", "Welcome", "<p>hi</p>", cts.Token));
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
            () => sender.SendAsync("ada@customer-mail.co", "Welcome", "<p>hi</p>"));

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
            () => sender.SendAsync("ada@customer-mail.co", "Welcome", TokenBody));

        Assert.DoesNotContain("super-secret-server-token", logger.Text);
        Assert.DoesNotContain("pkce_secret_9f8e7d6c", logger.Text);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", logger.Text);
        Assert.DoesNotContain("ada@customer-mail.co", logger.Text);
        // still useful for diagnosis — as stable diagnostics, not Postmark's free-form message
        Assert.Contains("406", logger.Text);
        Assert.Contains("InactiveRecipient", logger.Text);
        Assert.DoesNotContain("Inactive recipient", logger.Text);
    }

    // ---- provider message / subject personal-data leakage (P2) ------------------------------

    public static TheoryData<string, string[]> ProviderMessagesEchoingAddresses => new()
    {
        { "Inactive recipient user@example.com", ["user@example.com", "user@"] },
        {
            "Inactive recipients: alice.smith@acme.co.uk, bob_jones+hr@contoso.com; carol@example.org",
            ["alice.smith@acme.co.uk", "alice.smith", "bob_jones+hr@contoso.com", "bob_jones", "carol@example.org", "carol@"]
        },
        { "Inactive recipient Jane.Doe@Example.COM", ["Jane.Doe@Example.COM", "jane.doe"] },
        { "Recipient (<dave.oneil@example.net>). is inactive!", ["dave.oneil@example.net", "dave.oneil"] },
        {
            "Inactive recipient eve.leak@contoso.com token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.sig https://app/reset?token=pkce_echo_123",
            ["eve.leak@contoso.com", "eve.leak", "eyJhbGciOiJIUzI1NiJ9", "pkce_echo_123"]
        },
        { "Inactive recipient\r\nforged-line ctrl.char@contoso.com\u0007", ["ctrl.char@contoso.com", "ctrl.char", "forged-line"] },
    };

    [Theory]
    [MemberData(nameof(ProviderMessagesEchoingAddresses))]
    public async Task SendAsync_Failure_Provider_Message_Never_Reaches_Any_Log_Channel_Or_Exception(
        string providerMessage, string[] forbidden)
    {
        var logger = new CapturingLogger<PostmarkEmailSender>();
        var body = System.Text.Json.JsonSerializer.Serialize(new { ErrorCode = 406, Message = providerMessage });
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.UnprocessableEntity,
            ResponseBodyToReturn = body,
        };
        var sender = Build(handler, logger);

        HttpRequestException ex;
        using (logger.BeginScope(new Dictionary<string, object?> { ["Job"] = "EmailDeliveryJob" }))
        {
            ex = await Assert.ThrowsAsync<HttpRequestException>(
                () => sender.SendAsync("recipient.person@customer-mail.co", "Welcome", TokenBody));

            // Callers (EmailDeliveryJob / SendOperationalAlertEmailJob) log the thrown exception —
            // capture it through the same logger so the exception channel is inspected too.
            logger.LogWarning(ex, "caller logged delivery failure");
        }

        var all = logger.AllText;
        foreach (var value in forbidden)
            Assert.DoesNotContain(value, all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Inactive recipient", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recipient.person", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pkce_secret_9f8e7d6c", all);
        Assert.DoesNotContain("super-secret-server-token", all);
        Assert.DoesNotContain('@', all);

        // Status and ErrorCode stay diagnosable — as structured properties and in the exception.
        var entry = logger.Entries.First(e => e.Message.StartsWith("Postmark email send failed", StringComparison.Ordinal));
        Assert.Equal("422", entry.StateValue("StatusCode"));
        Assert.Equal("406", entry.StateValue("PostmarkErrorCode"));
        Assert.Equal("InactiveRecipient", entry.StateValue("FailureCategory"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("PostmarkErrorCode=406", ex.Message);
        Assert.Contains("FailureCategory=InactiveRecipient", ex.Message);
    }

    [Fact]
    public async Task SendAsync_Failure_Does_Not_Log_Subject_Containing_Personal_Data()
    {
        const string subject = "Offer letter for Jane Doe (jane.doe@acme.co.uk) at Acme Widgets Ltd";
        var logger = new CapturingLogger<PostmarkEmailSender>();
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.UnprocessableEntity,
            ResponseBodyToReturn = """{"ErrorCode":300,"Message":"Invalid email request"}""",
        };
        var sender = Build(handler, logger);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => sender.SendAsync("ada@customer-mail.co", subject, TokenBody));
        logger.LogWarning(ex, "caller logged delivery failure");

        var all = logger.AllText;
        Assert.DoesNotContain("Jane Doe", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jane.doe", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Acme Widgets", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Offer letter", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Subject", all, StringComparison.Ordinal);
        Assert.Equal("InvalidRequest", logger.Entries[0].StateValue("FailureCategory"));
    }

    [Fact]
    public async Task SendAsync_Skipped_Undeliverable_Recipient_Does_Not_Log_Subject()
    {
        const string subject = "Payslip for Jane Doe at Acme Widgets Ltd";
        var logger = new CapturingLogger<PostmarkEmailSender>();
        var handler = new FakeHttpMessageHandler();
        var sender = Build(handler, logger);

        await sender.SendAsync("jane.doe@acme.example", subject, TokenBody);

        Assert.Empty(handler.Requests);
        var all = logger.AllText;
        Assert.DoesNotContain("Jane Doe", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Acme Widgets", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jane.doe", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_5xx_Unparseable_Body_Is_Classified_Without_Logging_Body()
    {
        var logger = new CapturingLogger<PostmarkEmailSender>();
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.ServiceUnavailable,
            ResponseBodyToReturn = "<html>upstream down for someone@contoso.com</html>",
        };
        var sender = Build(handler, logger);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => sender.SendAsync("ada@customer-mail.co", "Welcome", TokenBody));
        logger.LogWarning(ex, "caller logged delivery failure");

        Assert.DoesNotContain("someone", logger.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upstream down", logger.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("503", logger.Entries[0].StateValue("StatusCode"));
        Assert.Null(logger.Entries[0].StateValue("PostmarkErrorCode"));
        Assert.Equal("ProviderUnavailable", logger.Entries[0].StateValue("FailureCategory"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, 406, nameof(PostmarkFailureCategory.InactiveRecipient))]
    [InlineData(HttpStatusCode.UnprocessableEntity, 300, nameof(PostmarkFailureCategory.InvalidRequest))]
    [InlineData(HttpStatusCode.UnprocessableEntity, 402, nameof(PostmarkFailureCategory.InvalidRequest))]
    [InlineData(HttpStatusCode.UnprocessableEntity, 400, nameof(PostmarkFailureCategory.SenderSignature))]
    [InlineData(HttpStatusCode.UnprocessableEntity, 401, nameof(PostmarkFailureCategory.SenderSignature))]
    [InlineData(HttpStatusCode.UnprocessableEntity, 405, nameof(PostmarkFailureCategory.AccountRestricted))]
    [InlineData(HttpStatusCode.UnprocessableEntity, 412, nameof(PostmarkFailureCategory.AccountRestricted))]
    [InlineData(HttpStatusCode.UnprocessableEntity, 1101, nameof(PostmarkFailureCategory.Template))]
    [InlineData(HttpStatusCode.Unauthorized, 10, nameof(PostmarkFailureCategory.Authentication))]
    [InlineData(HttpStatusCode.Unauthorized, null, nameof(PostmarkFailureCategory.Authentication))]
    [InlineData(HttpStatusCode.TooManyRequests, null, nameof(PostmarkFailureCategory.RateLimited))]
    [InlineData(HttpStatusCode.InternalServerError, null, nameof(PostmarkFailureCategory.ProviderUnavailable))]
    [InlineData(HttpStatusCode.UnprocessableEntity, 9999, nameof(PostmarkFailureCategory.Other))]
    [InlineData(HttpStatusCode.BadRequest, null, nameof(PostmarkFailureCategory.Other))]
    public void Classify_Maps_Known_Codes_To_Bounded_Categories(
        HttpStatusCode status, int? errorCode, string expected)
    {
        Assert.Equal(expected, PostmarkFailure.Classify(status, errorCode).ToString());
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
