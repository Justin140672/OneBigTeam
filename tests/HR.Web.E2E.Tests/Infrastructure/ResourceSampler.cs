using System.Diagnostics;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Background sampler that periodically logs host CPU/memory for the processes involved in an E2E
/// run (the Aspire-hosted dotnet processes — API/Web/AppHost — plus Postgres if it's running as a
/// plain process rather than in Docker) and a `docker stats` snapshot (for Postgres/anything else
/// Aspire runs as a container).
///
/// Exists to answer the open question from the laura.bennett login-timeout investigation: the login
/// cache's own coalescing/gating was fixed (see PersonaLoginCache's cooldown), but real logins still
/// time out waiting 45s for the app shell to render under 15-thread load. Before this, there was no
/// visibility into whether that's because the app/Postgres processes are actually CPU/memory
/// saturated at the moments those timeouts happen, or something else entirely. This logs through the
/// same E2eDiag channel as the login timing so both can be read side by side from one log.
///
/// Best-effort only: process CPU sampling and `docker stats` can both fail for all sorts of
/// environment reasons (process exited between snapshots, docker not on PATH, etc.) — none of that
/// should ever take down the actual test run, so every failure path here just logs and continues.
/// </summary>
internal static class ResourceSampler
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private static int _started;

    /// <summary>Idempotent — safe to call from every caller of SharedAppFixture.AcquireAsync; only the
    /// first call actually starts the background loop.</summary>
    public static void StartOnce()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;

        _ = Task.Run(async () =>
        {
            var previous = new Dictionary<int, (TimeSpan CpuTime, DateTime SampledAt)>();
            while (true)
            {
                try
                {
                    LogProcessSnapshot(previous);
                }
                catch (Exception ex)
                {
                    E2eDiag.Log("ResourceStats", $"process snapshot failed: {ex.GetType().Name}: {ex.Message}");
                }

                try
                {
                    await LogDockerStatsAsync();
                }
                catch (Exception ex)
                {
                    E2eDiag.Log("ResourceStats", $"docker stats failed: {ex.GetType().Name}: {ex.Message}");
                }

                await Task.Delay(Interval);
            }
            // ReSharper disable once FunctionNeverReturns — lives for the process, same as
            // SharedAppFixture's AppFixture itself; there is no run-scoped place to stop it from.
        });
    }

    // Names (case-insensitive substring) of processes worth tracking: the Aspire-hosted dotnet apps,
    // Postgres if run as a bare process, the browser/Node processes Playwright drives, and — critically —
    // "vmmem"/"vmmemwsl": Docker Desktop's WSL2/Hyper-V VM host process. Postgres actually runs INSIDE
    // that VM, not as a visible Windows process, so without this the host-process view can read
    // "4% CPU, all idle" while the VM housing the database is fully saturated — invisible from here.
    // A `docker stats` snapshot below is the other half of that picture (per-container, from inside
    // the VM); if `docker stats` itself is slow/unresponsive, Vmmem's own CPU/memory here is the one
    // signal from the Windows side that can still show the VM is in trouble.
    private static readonly string[] InterestingNameFragments =
        ["dotnet", "postgres", "chrome", "msedge", "playwright", "node", "vmmem"];

    private static void LogProcessSnapshot(Dictionary<int, (TimeSpan CpuTime, DateTime SampledAt)> previous)
    {
        var now = DateTime.UtcNow;
        var seen = new HashSet<int>();
        var samples = new List<(string Name, int Pid, double CpuPercent, long WorkingSetMb)>();
        var coreCount = Environment.ProcessorCount;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    if (!InterestingNameFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var cpuTime = process.TotalProcessorTime;
                    var workingSetMb = process.WorkingSet64 / (1024 * 1024);
                    seen.Add(process.Id);

                    double cpuPercent = 0;
                    if (previous.TryGetValue(process.Id, out var prior))
                    {
                        var cpuDelta = cpuTime - prior.CpuTime;
                        var wallDelta = now - prior.SampledAt;
                        if (wallDelta > TimeSpan.Zero)
                            cpuPercent = 100.0 * cpuDelta.TotalSeconds / (wallDelta.TotalSeconds * coreCount);
                    }

                    previous[process.Id] = (cpuTime, now);
                    samples.Add((name, process.Id, cpuPercent, workingSetMb));
                }
                catch
                {
                    // Process exited between GetProcesses() and reading its properties, or access
                    // denied — skip it, this is a best-effort diagnostic snapshot.
                }
            }
        }

        // Drop stale entries for processes that no longer exist so the dictionary doesn't grow
        // unbounded over a long parallel run spawning/killing many short-lived browser processes.
        foreach (var staleId in previous.Keys.Except(seen).ToList())
            previous.Remove(staleId);

        if (samples.Count == 0)
        {
            E2eDiag.Log("ResourceStats", "no matching processes found for this snapshot");
            return;
        }

        var totalCpu = samples.Sum(s => s.CpuPercent);
        var totalMemMb = samples.Sum(s => s.WorkingSetMb);
        var byName = samples
            .GroupBy(s => s.Name)
            .Select(g => new
            {
                Name = g.Key,
                Count = g.Count(),
                Cpu = g.Sum(s => s.CpuPercent),
                MemMb = g.Sum(s => s.WorkingSetMb),
            })
            .OrderByDescending(g => g.Cpu);

        E2eDiag.Log("ResourceStats",
            $"host processes: total CPU {totalCpu:F0}% of {coreCount * 100}% available ({coreCount} cores), total working set {totalMemMb:N0} MB | " +
            string.Join(", ", byName.Select(g => $"{g.Name} x{g.Count}: {g.Cpu:F0}% CPU, {g.MemMb:N0} MB")));
    }

    private static async Task LogDockerStatsAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "stats --no-stream --format \"{{.Name}}: {{.CPUPerc}} CPU, {{.MemUsage}}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            E2eDiag.Log("ResourceStats", "docker stats: failed to start process");
            return;
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        // Compare against the SAME task instance passed to WhenAny. Previously this compared against
        // a second, freshly-created process.WaitForExitAsync() task — never reference-equal — so it
        // ALWAYS took the "timed out" branch immediately (logged at +0.1s, not after 10s) and
        // Kill(entireProcessTree) a docker CLI that was still mid-request to Docker Desktop, every
        // 15s for the whole run.
        var exitTask = process.WaitForExitAsync();
        var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(10)));
        if (completed != exitTask)
        {
            E2eDiag.Log("ResourceStats", "docker stats: timed out after 10s, skipping this snapshot");
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return;
        }

        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();

        if (process.ExitCode != 0 || stdout.Length == 0)
        {
            E2eDiag.Log("ResourceStats", $"docker stats: exit {process.ExitCode}, stderr: {stderr}");
            return;
        }

        E2eDiag.Log("ResourceStats", $"docker containers: {stdout.Replace(Environment.NewLine, " | ")}");
    }
}
