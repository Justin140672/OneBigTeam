using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Api.Startup;

/// <summary>
/// Runs the required per-module database migrate + seed steps in a fixed order during API startup.
///
/// <para>
/// Every module migration is <b>required</b>: if any step throws, the API must not begin serving
/// normal traffic and must not register Hangfire recurring jobs. Instead the process stays up in a
/// clearly-defined non-ready state that exposes only health information — <c>/health/startup-migrations</c>
/// returns 503 with the affected module, and the <c>startup-migrations</c> readiness health check
/// reports Unhealthy (critical) so <c>/health/ready</c> returns 503 and orchestrators will not route
/// traffic to the instance.
/// </para>
///
/// <para>
/// This replaces the previous ~260 lines of copy-pasted, subtly-divergent per-module try/catch
/// blocks that swallowed every failure and let the API start anyway.
/// </para>
/// </summary>
internal sealed class StartupMigrationRunner(ILogger<StartupMigrationRunner> logger)
{
    private readonly List<StepResult> _results = [];
    private readonly object _gate = new();

    private sealed record StepResult(string Module, string Status, DateTimeOffset CheckedAt, string? Error);

    public bool AllSucceeded
    {
        get { lock (_gate) { return _results.All(r => r.Status == "succeeded"); } }
    }

    public IReadOnlyList<string> FailedModules
    {
        get { lock (_gate) { return _results.Where(r => r.Status != "succeeded").Select(r => r.Module).ToArray(); } }
    }

    /// <summary>
    /// Executes one required migration step. Ordering matters, so callers must await each call in
    /// sequence. A failure is logged (with the affected module and the exception) and recorded, but
    /// does not throw — the caller inspects <see cref="AllSucceeded"/> afterwards and decides not to
    /// build the normal request pipeline.
    /// </summary>
    public async Task RunAsync(string module, IServiceProvider services, Func<IServiceProvider, Task> migrateAndSeed)
    {
        try
        {
            await migrateAndSeed(services);
            Record(new StepResult(module, "succeeded", DateTimeOffset.UtcNow, null));
            logger.LogInformation("Startup migration succeeded for module {Module}", module);
        }
        catch (Exception exception)
        {
            Record(new StepResult(module, "failed", DateTimeOffset.UtcNow, exception.Message));
            logger.LogCritical(
                exception,
                "Startup migration failed for module {Module}. The API will not serve normal traffic "
                + "or register background jobs until this is resolved.",
                module);
        }
    }

    private void Record(StepResult result)
    {
        lock (_gate) { _results.Add(result); }
    }

    /// <summary>Builds the <c>/health/startup-migrations</c> payload (shape unchanged: module name -&gt; status/checkedAt/error).</summary>
    public IResult ToHealthResult()
    {
        Dictionary<string, object> payload;
        bool allSucceeded;
        lock (_gate)
        {
            payload = _results.ToDictionary(
                r => r.Module,
                r => (object)new { status = r.Status, checkedAt = r.CheckedAt, error = r.Error });
            allSucceeded = _results.All(r => r.Status == "succeeded");
        }

        return allSucceeded
            ? Results.Ok(payload)
            : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

/// <summary>
/// Readiness health check (tagged <c>critical</c>) so <c>/health/ready</c> returns 503 while any
/// required migration is unresolved. Never discloses the underlying exception detail.
/// </summary>
internal sealed class StartupMigrationHealthCheck(StartupMigrationRunner runner) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(runner.AllSucceeded
            ? HealthCheckResult.Healthy("All required module migrations applied.")
            : HealthCheckResult.Unhealthy(
                $"Required module migration(s) failed: {string.Join(", ", runner.FailedModules)}."));
}
