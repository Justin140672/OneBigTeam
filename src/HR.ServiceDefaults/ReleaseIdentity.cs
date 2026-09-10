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
    /// <summary>UTC instant this process started — a cheap "is this a fresh instance?" signal.</summary>
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

    /// <summary>
    /// Maps the running assembly name to the Railway service slug used by the deploy pipeline.
    /// <c>RELEASE_SERVICE</c> overrides it for any host whose assembly name does not follow the
    /// <c>HR.*</c> convention.
    /// </summary>
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

    /// <summary>
    /// The <c>/health/release</c> payload. <c>service</c>/<c>sha</c>/<c>version</c>/<c>environment</c>
    /// are mutable display labels the pipeline injects. <c>deploymentId</c>/<c>railwayServiceId</c>/
    /// <c>railwayEnvironmentId</c>/<c>railwayCommit</c> are Railway-injected immutable identity — the
    /// deploy pipeline never sets them — and are the fields recovery correlates against the Railway
    /// API's serving-deployment record. <c>railwayCommit</c> may be null (non-git deploys).
    /// </summary>
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

    /// <summary>The <c>{ sha, version }</c> sub-object embedded in other health payloads (e.g. startup-migrations).</summary>
    public static object ReleaseTag(Assembly? assembly = null) => new
    {
        sha = Sha(assembly),
        version = Version(assembly),
    };

    /// <summary>Extracts the git commit SHA embedded in an <c>AssemblyInformationalVersion</c> string, or <c>null</c>.</summary>
    public static string? ShaFromInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        // Deterministic CI builds stamp AssemblyInformationalVersion as
        //   "<version>+<sha>"   or   "<version-with-build-metadata>.<sha>"
        // when the supplied Version already carries "+<short_sha>" build metadata. Take the trailing
        // token after the last '+' and, if that still contains '.', its last dotted segment.
        var afterPlus = informationalVersion.Contains('+')
            ? informationalVersion[(informationalVersion.LastIndexOf('+') + 1)..]
            : informationalVersion;

        var candidate = afterPlus.Contains('.')
            ? afterPlus[(afterPlus.LastIndexOf('.') + 1)..]
            : afterPlus;

        candidate = candidate.Trim();

        // Only accept a full 40-hex git SHA. A local build with no SourceRevisionId leaves just the
        // short build-metadata token (e.g. "abc1234") or a version segment ("123") — neither can be
        // safely compared against the full-SHA release target, so report nothing and let the
        // RELEASE_SHA env override (which CI always sets) be the source of truth instead.
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
