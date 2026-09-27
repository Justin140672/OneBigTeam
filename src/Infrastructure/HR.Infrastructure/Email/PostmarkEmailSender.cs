using System.Net.Http.Json;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Email;

/// <summary>
/// Sends transactional email via the Postmark HTTP API.
/// Used whenever Infrastructure:Postmark:ServerToken is configured; otherwise
/// <see cref="LoggingEmailSender"/> is registered instead — see InfrastructureModule.
/// </summary>
internal sealed class PostmarkEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly PostmarkOptions _options;
    private readonly ILogger<PostmarkEmailSender> _logger;

    public PostmarkEmailSender(HttpClient httpClient, IOptions<PostmarkOptions> options, ILogger<PostmarkEmailSender> logger)
    {
        _httpClient = httpClient;
        _options    = options.Value;
        _logger     = logger;

        _httpClient.BaseAddress = new Uri("https://api.postmarkapp.com/");
        _httpClient.DefaultRequestHeaders.Add("X-Postmark-Server-Token", _options.ServerToken);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (PostmarkRecipientGuard.IsUndeliverable(toEmail))
        {
            // Reserved / never-deliverable domain (RFC 2606, or this app's *.example seed personas).
            // Drop it silently — returning normally (not throwing) so EmailDeliveryJob does not retry
            // or mark a spurious hard failure. See PostmarkRecipientGuard.
            _logger.LogWarning(
                "Postmark send skipped: recipient domain is a reserved / undeliverable address. " +
                "A live Postmark token is likely configured in a non-production environment.");
            return;
        }

        var payload = new
        {
            From          = _options.FromEmail,
            To            = toEmail,
            Subject       = subject,
            HtmlBody      = htmlBody,
            MessageStream = _options.MessageStream,
        };

        using var response = await _httpClient.PostAsJsonAsync("email", payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            // Log and throw only stable diagnostics: HTTP status, Postmark's numeric ErrorCode and a
            // bounded failure category. Never the response body, Postmark's free-form Message (it can
            // echo the rejected recipient address, e.g. "Inactive recipient user@example.com"), the
            // caller-controlled subject (it can carry names or company names), the recipient, or the
            // htmlBody (single-use tokens and secure action links).
            var failure = await PostmarkFailure.FromResponseAsync(response, ct);
            _logger.LogWarning(
                "Postmark email send failed. StatusCode={StatusCode} PostmarkErrorCode={PostmarkErrorCode} FailureCategory={FailureCategory}",
                failure.StatusCode, failure.ErrorCode, failure.Category);

            // A fixed-shape exception instead of EnsureSuccessStatusCode, so the exception text that
            // EmailDeliveryJob / SendOperationalAlertEmailJob log is fully under our control (no
            // provider-supplied reason phrase). StatusCode is preserved so callers can still tell a
            // terminal 4xx from a retryable 5xx.
            throw new HttpRequestException(
                failure.ToExceptionMessage("Postmark email send"), inner: null, response.StatusCode);
        }
    }
}
