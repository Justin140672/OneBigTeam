using System.Diagnostics;
using System.Reflection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Ticket 5 — the running release identity of a deployed service, exposed anonymously at
/// <c>GET /health/release</c> so a deployment pipeline can verify that the <b>exact</b> requested
/// release is actually running on every required service (a healthy old instance reporting the old
/// SHA must fail verification).
///
/// <para>
/// Resolution order, both values independently overridable without a rebuild:
/// <list type="bullet">
/// <item><c>sha</c> — <c>RELEASE_SHA</c> env var, else the git commit parsed from
/// <see cref="AssemblyInformationalVersionAttribute"/> (CI stamps <c>-p:SourceRevisionId=&lt;full_sha&gt;</c>),
/// else <c>"unknown"</c>.</item>
/// <item><c>version</c> — <c>PLATFORM_VERSION</c> env var, else <see cref="AssemblyInformationalVersionAttribute"/>,
/// else the plain assembly version, else <c>"unknown"</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// The payload deliberately discloses only service slug, sha, version, environment and process
/// start time — never hosts, connection strings, credentials or infrastructure detail — consistent
/// with the health-response rules in <c>08-deployment-architecture.md</c>.
/// </para>
/// </summary>
public static class ReleaseIdentity
{
    public static readonly DateTimeOffset StartedAtUtc =
        Process.GetCurrentProcess().StartTime.ToUniversalTime();

    /// <summary>
    /// Ticket 5 follow-up (defect 2) — the immutable, Railway-assigned identity of the *running*
    /// deployment instance, read from Railway-provided environment variables that the deploy
    /// pipeline NEVER sets. This is the trustworthy serving-instance identity anchor: unlike
    /// <c>RELEASE_SHA</c> / <c>PLATFORM_VERSION</c> (mutable display labels the pipeline injects),
    /// <c>RAILWAY_DEPLOYMENT_ID</c> cannot be spoofed by a variable reset, so recovery can prove the
    /// instance answering the public URL is the exact deployment a rollback restored.
    ///
    /// <para><c>railwayCommit</c> (<c>RAILWAY_GIT_COMMIT_SHA</c>) is null for non-git deploys such as
    /// <c>railway up</c>; callers must treat null as "no git identity available", not a mismatch.</para>
    /// </summary>
    public static string? DeploymentId() => Trimmed(Environment.GetEnvironmentVariable("RAILWAY_DEPLOYMENT_ID"));

    public static string? RailwayServiceId() => Trimmed(Environment.GetEnvironmentVariable("RAILWAY_SERVICE_ID"));

    public static string? RailwayEnvironmentId() => Trimmed(Environment.GetEnvironmentVariable("RAILWAY_ENVIRONMENT_ID"));

    public static string? RailwayCommit() => Trimmed(Environment.GetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA"));

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Sha(Assembly? assembly = null)
    {
        var overridden = Environment.GetEnvironmentVariable("RELEASE_SHA");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden.Trim();
        }

        return ShaFromInformationalVersion(InformationalVersion(assembly)) ?? "unknown";
    }

    public static string Version(Assembly? assembly = null)
    {
        var overridden = Environment.GetEnvironmentVariable("PLATFORM_VERSION");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden.Trim();
        }

        var informational = InformationalVersion(assembly);
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational!;
        }

        return (assembly ?? EntryAssembly())?.GetName().Version?.ToString() ?? "unknown";
    }

    public static string Service(string? applicationName = null)
    {
        var overridden = Environment.GetEnvironmentVariable("RELEASE_SERVICE");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden.Trim();
        }

        return (applicationName ?? EntryAssembly()?.GetName().Name) switch
        {
            "HR.Api" => "api",
            "HR.Web" => "app",
            "HR.Marketing" => "marketing",
            "HR.Admin.Web" => "admin",
            "HR.Admin.Api" => "admin-api",
            var other when !string.IsNullOrWhiteSpace(other) => other!.ToLowerInvariant(),
            _ => "unknown",
        };
    }

    public static object Payload(string environmentName, string? applicationName = null, Assembly? assembly = null) => new
    {
        service = Service(applicationName),
        sha = Sha(assembly),
        version = Version(assembly),
        environment = environmentName,
        startedAt = StartedAtUtc,
        deploymentId = DeploymentId(),
        railwayServiceId = RailwayServiceId(),
        railwayEnvironmentId = RailwayEnvironmentId(),
        railwayCommit = RailwayCommit(),
    };

    public static object ReleaseTag(Assembly? assembly = null) => new
    {
        sha = Sha(assembly),
        version = Version(assembly),
    };

    public static string? ShaFromInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var afterPlus = informationalVersion.Contains('+')
            ? informationalVersion[(informationalVersion.LastIndexOf('+') + 1)..]
            : informationalVersion;

        var candidate = afterPlus.Contains('.')
            ? afterPlus[(afterPlus.LastIndexOf('.') + 1)..]
            : afterPlus;

        candidate = candidate.Trim();

        var looksLikeSha = candidate.Length == 40
            && candidate.All(Uri.IsHexDigit);

        return looksLikeSha ? candidate.ToLowerInvariant() : null;
    }

    private static string? InformationalVersion(Assembly? assembly) =>
        (assembly ?? EntryAssembly())
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

    private static Assembly? EntryAssembly() => Assembly.GetEntryAssembly();
}
