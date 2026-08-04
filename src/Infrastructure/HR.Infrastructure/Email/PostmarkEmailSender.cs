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
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Postmark email send failed. To={ToEmail} StatusCode={StatusCode} Body={Body}",
                toEmail, (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }
    }
}
