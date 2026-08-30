using System.Net.Http.Json;
using HR.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Email;

/// <summary>
/// Sends password-reset emails via the Postmark <c>/email/withTemplate</c> endpoint using the
/// configured <c>password-reset</c> template alias. Mirrors <see cref="PostmarkInvitationEmailSender"/>.
/// </summary>
internal sealed class PostmarkPasswordResetEmailSender : IPasswordResetEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly PostmarkOptions _postmark;
    private readonly EmailBrandingOptions _branding;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostmarkPasswordResetEmailSender> _logger;

    private const string FallbackBaseUrl = "http://localhost:5157";

    public PostmarkPasswordResetEmailSender(
        HttpClient httpClient,
        IOptions<PostmarkOptions> postmarkOptions,
        IOptions<EmailBrandingOptions> brandingOptions,
        IConfiguration configuration,
        ILogger<PostmarkPasswordResetEmailSender> logger)
    {
        _httpClient    = httpClient;
        _postmark      = postmarkOptions.Value;
        _branding      = brandingOptions.Value;
        _configuration = configuration;
        _logger        = logger;

        _httpClient.BaseAddress = new Uri("https://api.postmarkapp.com/");
        _httpClient.DefaultRequestHeaders.Add("X-Postmark-Server-Token", _postmark.ServerToken);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<bool> SendAsync(
        string toEmail,
        string? recipientName,
        string actionUrl,
        string? userAgent,
        CancellationToken ct = default)
    {
        var templateAlias = _postmark.PasswordResetTemplateAlias;
        if (string.IsNullOrWhiteSpace(templateAlias))
        {
            _logger.LogError(
                "Postmark password-reset template alias is not configured. " +
                "Set Infrastructure:Postmark:PasswordResetTemplateAlias.");
            return false;
        }

        var productUrl = _configuration["WebApp:BaseUrl"]?.TrimEnd('/') ?? FallbackBaseUrl;

        var payload = new
        {
            From          = _postmark.FromEmail,
            To            = toEmail,
            TemplateAlias = templateAlias,
            MessageStream = _postmark.MessageStream,
            TemplateModel = BuildTemplateModel(_branding, productUrl, recipientName, actionUrl, userAgent),
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("email/withTemplate", payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                // Log status code but never the action URL — it carries the single-use recovery token.
                _logger.LogWarning(
                    "Postmark password-reset email send failed. StatusCode={StatusCode} To={ToEmail}",
                    (int)response.StatusCode,
                    toEmail);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Postmark password-reset email request failed. To={ToEmail}",
                toEmail);
            return false;
        }
    }

    /// <summary>
    /// Builds the Postmark <c>password-reset</c> template model. Extracted for unit testing —
    /// the field set and naming is part of the template contract.
    /// </summary>
    internal static Dictionary<string, string> BuildTemplateModel(
        EmailBrandingOptions branding,
        string productUrl,
        string? recipientName,
        string actionUrl,
        string? userAgent)
    {
        var ua = UserAgentSummary.Parse(userAgent);

        return new Dictionary<string, string>
        {
            ["product_url"]      = productUrl,
            ["product_name"]     = branding.ProductName,
            ["name"]             = recipientName ?? string.Empty,
            ["action_url"]       = actionUrl,
            ["operating_system"] = ua.OperatingSystem,
            ["browser_name"]     = ua.BrowserName,
            ["support_url"]      = branding.SupportUrl ?? string.Empty,
            ["company_name"]     = branding.CompanyName,
            ["company_address"]  = branding.CompanyAddress ?? string.Empty,
        };
    }
}
