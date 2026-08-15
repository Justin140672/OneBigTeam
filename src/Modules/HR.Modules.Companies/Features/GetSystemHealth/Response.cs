namespace HR.Modules.Companies.Features.GetSystemHealth;

/// <summary>
/// One entry per platform capability shown on the System Health Dashboard (Platform Monitoring
/// epic). Status is one of "Healthy", "Degraded" or "Unhealthy" — a direct string projection of
/// ASP.NET Core's <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus"/> enum, so
/// the Admin Portal UI doesn't need a reference to that framework type.
/// </summary>
internal sealed record SystemHealthCategory(string Name, string Status, string? Description);

internal sealed record GetSystemHealthResponse(
    string OverallStatus,
    string PlatformVersion,
    DateTimeOffset CheckedAt,
    IReadOnlyList<SystemHealthCategory> Categories);
