using HR.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace HR.Infrastructure.Email;

/// <summary>
/// Builds invite links using the configured base URL of the HR web application.
/// Configure via AppSettings key "WebApp:BaseUrl" (e.g. "https://app.example.com").
/// Falls back to a localhost development URL when not configured.
/// </summary>
internal sealed class ConfiguredInviteLinkBuilder(IConfiguration configuration) : IInviteLinkBuilder
{
    private const string FallbackBaseUrl = "http://localhost:5270";

    public string Build(string token)
    {
        var baseUrl = configuration["WebApp:BaseUrl"]?.TrimEnd('/') ?? FallbackBaseUrl;
        return $"{baseUrl}/invite/{Uri.EscapeDataString(token)}";
    }
}
