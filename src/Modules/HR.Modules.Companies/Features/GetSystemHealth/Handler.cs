using System.Reflection;

using HR.SharedKernel;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Modules.Companies.Features.GetSystemHealth;

/// <summary>
/// Platform-wide (not scoped to one customer) System Health Dashboard (Platform Monitoring epic).
/// Same defense-in-depth allow-list gate as ListBackgroundJobsHandler (see its remarks) — no
/// first-class platform-administrator identity model exists yet.
///
/// Deliberately reads through the framework's <see cref="HealthCheckService"/> — which aggregates
/// every named IHealthCheck registered across HR.Api's startup (database/stripe in
/// CompaniesModule, auth in IdentityModule, email/storage in InfrastructureModule, hangfire in
/// InfrastructureModule's AddHangfireBackgroundJobs) — rather than re-implementing each
/// integration's connectivity check in this handler. This keeps ownership of each check with the
/// module/project that owns that integration, matching the "no cross-module references" rule:
/// HealthCheckService is a framework-provided aggregator, not another module's internals.
/// </summary>
internal sealed class GetSystemHealthHandler(
    HealthCheckService healthCheckService,
    ICurrentUser currentUser,
    IConfiguration configuration)
{
    private static readonly IReadOnlyList<(string Key, string DisplayName)> CategoryOrder =
    [
        ("database", "Database"),
        ("storage", "Storage"),
        ("auth", "Authentication"),
        ("email", "Email"),
        ("stripe", "Stripe"),
        ("hangfire", "Background Jobs"),
    ];

    public async Task<Result<GetSystemHealthResponse>> HandleAsync(
        GetSystemHealthRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<GetSystemHealthResponse>(
                Error.Unauthorized("This account is not authorised to view platform health data."));
        }

        var report = await healthCheckService.CheckHealthAsync(cancellationToken);

        var categories = CategoryOrder
            .Select(category =>
            {
                if (report.Entries.TryGetValue(category.Key, out var entry))
                {
                    return new SystemHealthCategory(
                        category.DisplayName, entry.Status.ToString(), entry.Description);
                }

                // A check that hasn't been registered (e.g. missing configuration wiring) is
                // surfaced as Unhealthy rather than silently omitted, so a wiring regression is
                // visible on the dashboard rather than hidden.
                return new SystemHealthCategory(
                    category.DisplayName, HealthStatus.Unhealthy.ToString(), "No health check registered.");
            })
            .ToList();

        var response = new GetSystemHealthResponse(
            report.Status.ToString(),
            GetPlatformVersion(),
            DateTimeOffset.UtcNow,
            categories);

        return Result.Success(response);
    }

    /// <summary>
    /// Prefers an explicit "Platform:Version" configuration value (e.g. an env var a future
    /// deployment pipeline can set to a git SHA/build number) and falls back to the running entry
    /// assembly's informational version, then its plain assembly version, then "unknown". No CI/CD
    /// build-number injection exists yet (see deployment architecture spec's future-evolution
    /// notes) — this is a deliberately lightweight mechanism that a later story can wire a real
    /// value into without further code changes here.
    /// </summary>
    private static string GetPlatformVersion()
    {
        var configuredVersion = System.Environment.GetEnvironmentVariable("PLATFORM_VERSION");
        if (!string.IsNullOrWhiteSpace(configuredVersion))
            return configuredVersion;

        var entryAssembly = Assembly.GetEntryAssembly();
        var informationalVersion = entryAssembly
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion;

        return entryAssembly?.GetName().Version?.ToString() ?? "unknown";
    }

    private bool IsAllowListedPlatformAdmin()
    {
        var email = currentUser.Email;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var allowedEmails = configuration.GetSection("PlatformAdmin:AllowedEmails").Get<string[]>()
            ?? [];

        return allowedEmails.Any(allowed =>
            string.Equals(allowed, email, StringComparison.OrdinalIgnoreCase));
    }
}
