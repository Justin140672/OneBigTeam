using System.Diagnostics;

namespace HR.Web.E2E.Tests.Infrastructure;

internal static class ResourceSampler
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private static int _started;

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
        });
    }

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
                }
            }
        }

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
