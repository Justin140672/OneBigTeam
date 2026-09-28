namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Configuration for development-only tools and endpoints.
///
/// DevTools provides minting access tokens, creating development users, and switching personas
/// for local development. These capabilities require three conditions:
/// 1. Development environment (enforced at startup)
/// 2. Explicit DevTools.Enabled=true opt-in (not enabled by default)
/// 3. Loopback/local request (127.0.0.1, ::1; enforced by LoopbackOnlyMiddleware)
///
/// Endpoints are logged for audit purposes with restricted detail (tokens are never logged).
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
