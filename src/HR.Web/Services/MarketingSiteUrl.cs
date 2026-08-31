using Microsoft.Extensions.Configuration;

namespace HR.Web.Services;

/// <summary>
/// Resolves the base URL of the One Big Team marketing website, which in production is a
/// different domain from the HR application and per launch (under Aspire) a dynamically-assigned
/// URL. Reads Aspire's service-discovery keys — the same
/// <c>services:marketing:https:0</c> / <c>services:marketing:http:0</c> pattern used elsewhere
/// in this codebase (e.g. ResendVerificationHandler, VerifyEmailError.razor) — never a
/// hard-coded domain. The local dev fallback matches HR.Marketing's launch profile port.
/// </summary>
public static class MarketingSiteUrl
{
    /// <summary>Trimmed marketing base URL with no trailing slash, e.g. <c>https://onebigteam.co.uk</c>.</summary>
    public static string Resolve(IConfiguration configuration)
    {
        var baseUrl =
            configuration["services:marketing:https:0"] ??
            configuration["services:marketing:http:0"] ??
            "http://localhost:5166";

        return baseUrl.TrimEnd('/');
    }
}
