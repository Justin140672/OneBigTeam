namespace HR.Api.Startup;

/// <summary>
/// Configuration for development-only tools and endpoints.
///
/// DevTools provides minting access tokens, creating development users, and switching personas
/// for local development. These capabilities are:
/// 1. Only available when explicitly enabled via configuration
/// 2. Only available in Development environment (enforced at startup)
/// 3. Restricted to loopback addresses when running in production-like environments
/// 4. Logged for audit purposes with restricted detail (tokens are never logged)
/// </summary>
public class DevToolsOptions
{
    public const string SectionName = "DevTools";

    /// <summary>
    /// Enable development tools and related endpoints. Defaults to false.
    ///
    /// This setting must be explicitly set to true in appsettings.Development.json
    /// to enable the /api/dev/* endpoints. It is ignored in non-Development environments
    /// and attempting to set it to true in Staging or Production will fail at startup.
    /// </summary>
    public bool Enabled { get; set; } = false;
}
