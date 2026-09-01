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
            // Log only Postmark's own error code/message, never the full response body or the
            // htmlBody we sent — transactional email bodies carry single-use tokens and secure
            // action links.
            var (errorCode, message) = await ReadPostmarkErrorAsync(response, ct);
            _logger.LogWarning(
                "Postmark email send failed. To={ToEmail} StatusCode={StatusCode} PostmarkErrorCode={PostmarkErrorCode} PostmarkMessage={PostmarkMessage}",
                SensitiveDataScrubber.MaskEmail(toEmail), (int)response.StatusCode, errorCode, message);
            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task<(int? ErrorCode, string Message)> ReadPostmarkErrorAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(raw))
                return (null, "(no response body)");

            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            int? code = root.TryGetProperty("ErrorCode", out var c) && c.TryGetInt32(out var ci) ? ci : null;
            var msg = root.TryGetProperty("Message", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String
                ? SensitiveDataScrubber.ScrubText(m.GetString())
                : "(no message)";
            return (code, msg);
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, "(unparseable response body)");
        }
    }
}
