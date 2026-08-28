using System.Net.Http.Json;
using HR.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Email;

/// <summary>
/// Sends employee invitation emails via the Postmark /email/withTemplate endpoint
/// using the configured <c>user-invitation</c> template alias.
/// </summary>
internal sealed class PostmarkInvitationEmailSender : IInvitationEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly PostmarkOptions _postmark;
    private readonly EmailBrandingOptions _branding;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostmarkInvitationEmailSender> _logger;

    private const string FallbackBaseUrl = "http://localhost:5157";

    public PostmarkInvitationEmailSender(
        HttpClient httpClient,
        IOptions<PostmarkOptions> postmarkOptions,
        IOptions<EmailBrandingOptions> brandingOptions,
        IConfiguration configuration,
        ILogger<PostmarkInvitationEmailSender> logger)
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
        CancellationToken ct = default)
    {
        var templateAlias = _postmark.InvitationTemplateAlias;
        if (string.IsNullOrWhiteSpace(templateAlias))
        {
            _logger.LogError(
                "Postmark invitation template alias is not configured. " +
                "Set Infrastructure:Postmark:InvitationTemplateAlias.");
            return false;
        }

        var productUrl = _configuration["WebApp:BaseUrl"]?.TrimEnd('/') ?? FallbackBaseUrl;

        var payload = new
        {
            From          = _postmark.FromEmail,
            To            = toEmail,
            TemplateAlias = templateAlias,
            MessageStream = _postmark.MessageStream,
            TemplateModel = new
            {
                product_name    = _branding.ProductName,
                product_url     = productUrl,
                logo_url        = _branding.LogoUrl ?? string.Empty,
                name            = recipientName ?? string.Empty,
                action_url      = actionUrl,
                support_email   = _branding.SupportEmail ?? string.Empty,
                company_name    = _branding.CompanyName,
                company_address = _branding.CompanyAddress ?? string.Empty,
            },
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("email/withTemplate", payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                // Log status code but not the action URL which contains the invitation token.
                _logger.LogWarning(
                    "Postmark invitation email send failed. StatusCode={StatusCode} To={ToEmail}",
                    (int)response.StatusCode,
                    toEmail);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Postmark invitation email request failed. To={ToEmail}",
                toEmail);
            return false;
        }
    }
}
