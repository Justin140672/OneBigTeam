using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit.Abstractions;

namespace HR.Integration.Tests.Performance;

/// <summary>
/// NFR-02 measurement harness. Warms up, then runs N measured iterations of an operation, capturing
/// wall-clock latency and EF command count per iteration. Reports p95 latency (the agreed metric for
/// API/page operations — see docs/performance-testing.md), plus median / p99 / max and cold-vs-warm,
/// and records the run to <see cref="PerformanceResults"/> for artefact output.
///
/// The pass/fail budget is <c>target * PERF_CI_MULTIPLIER</c>. The product targets
/// (specifications/product-specifications/31-non-functional-requirements.md) assume production
/// hardware; shared CI runners are slower and noisier, so the multiplier (default 3.0) keeps the
/// gate meaningful without flaky-failing. Set <c>PERF_CI_MULTIPLIER=1</c> to assert the raw product
/// target on a representative runner.
/// </summary>
internal sealed class PerformanceMeasurement
{
    private readonly ITestOutputHelper _output;

    public PerformanceMeasurement(ITestOutputHelper output) => _output = output;

    public static int WarmupIterations =>
        ReadInt("PERF_WARMUP_ITERATIONS", 2);

    public static int MeasuredIterations =>
        ReadInt("PERF_ITERATIONS", 15);

    public static double CiMultiplier =>
        double.TryParse(
            Environment.GetEnvironmentVariable("PERF_CI_MULTIPLIER"),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var m) && m > 0
            ? m
            : 3.0;

    public async Task<PerfResult> MeasureAsync(
        string operation,
        string scaleLabel,
        int scale,
        double targetMs,
        Func<Task> action)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            await action();
        }

        var latencies = new List<double>(MeasuredIterations);
        var commandCounts = new List<int>(MeasuredIterations);
        var slowCommands = new List<SlowCommand>();

        for (var i = 0; i < MeasuredIterations; i++)
        {
            using (QueryCountingInterceptor.BeginScope())
            {
                var sw = Stopwatch.StartNew();
                await action();
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);

                var state = QueryCountingInterceptor.ActiveState;
                commandCounts.Add(state?.CommandCount ?? 0);
                if (state is not null)
                {
                    lock (state.SlowCommands)
                    {
                        slowCommands.AddRange(state.SlowCommands);
                    }
                }
            }
        }

        var result = new PerfResult
        {
            Operation = operation,
            ScaleLabel = scaleLabel,
            Scale = scale,
            TargetMs = targetMs,
            CiMultiplier = CiMultiplier,
            BudgetMs = targetMs * CiMultiplier,
            Iterations = MeasuredIterations,
            ColdMs = Math.Round(latencies[0], 1),
            MedianMs = Math.Round(Percentile(latencies, 50), 1),
            P95Ms = Math.Round(Percentile(latencies, 95), 1),
            P99Ms = Math.Round(Percentile(latencies, 99), 1),
            MaxMs = Math.Round(latencies.Max(), 1),
            MinCommandCount = commandCounts.Min(),
            MaxCommandCount = commandCounts.Max(),
            SlowCommandCount = slowCommands.Count,
            CapturedUtc = DateTimeOffset.UtcNow,
        };

        _output.WriteLine(result.ToString());
        foreach (var slow in slowCommands.OrderByDescending(s => s.Duration).Take(3))
        {
            _output.WriteLine($"  slow ({slow.Duration.TotalMilliseconds:F0} ms): {Truncate(slow.CommandText, 200)}");
        }

        PerformanceResults.Record(result);
        return result;
    }

    private static double Percentile(List<double> values, double percentile)
    {
        var ordered = values.OrderBy(v => v).ToList();
        if (ordered.Count == 1)
        {
            return ordered[0];
        }

        var rank = percentile / 100.0 * (ordered.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return ordered[low] + (rank - low) * (ordered[high] - ordered[low]);
    }

    private static int ReadInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}

internal sealed class PerfResult
{
    public string Operation { get; init; } = "";
    public string ScaleLabel { get; init; } = "";
    public int Scale { get; init; }
    public double TargetMs { get; init; }
    public double CiMultiplier { get; init; }
    public double BudgetMs { get; init; }
    public int Iterations { get; init; }
    public double ColdMs { get; init; }
    public double MedianMs { get; init; }
    public double P95Ms { get; init; }
    public double P99Ms { get; init; }
    public double MaxMs { get; init; }
    public int MinCommandCount { get; init; }
    public int MaxCommandCount { get; init; }
    public int SlowCommandCount { get; init; }
    public DateTimeOffset CapturedUtc { get; init; }

    [JsonIgnore]
    public bool WithinBudget => P95Ms <= BudgetMs;

    public override string ToString() =>
        $"[PERF] {Operation} @ {ScaleLabel} ({Scale}): cold={ColdMs}ms median={MedianMs}ms " +
        $"p95={P95Ms}ms p99={P99Ms}ms max={MaxMs}ms | target={TargetMs}ms budget={BudgetMs}ms (x{CiMultiplier}) " +
        $"| commands/req={MinCommandCount}..{MaxCommandCount} slow={SlowCommandCount}";
}

/// <summary>
/// Process-wide sink for <see cref="PerfResult"/>s. Test parallelization is disabled for this
/// assembly, so a simple locked list + rewrite-on-each-record is safe and guarantees a complete
/// artefact even if the run is interrupted. Output path: <c>PERF_RESULTS_PATH</c> env var, else
/// <c>./perf-results/perf-results.json</c> under the test working directory.
/// </summary>
internal static class PerformanceResults
{
    private static readonly object Gate = new();
    private static readonly List<PerfResult> Results = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string OutputPath =>
        Environment.GetEnvironmentVariable("PERF_RESULTS_PATH")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "perf-results", "perf-results.json");

    public static void Record(PerfResult result)
    {
        lock (Gate)
        {
            Results.Add(result);
            Flush();
        }
    }

    private static void Flush()
    {
        var path = OutputPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var document = new
        {
            schemaVersion = 1,
            generatedUtc = DateTimeOffset.UtcNow,
            machineName = Environment.MachineName,
            processorCount = Environment.ProcessorCount,
            ciMultiplier = PerformanceMeasurement.CiMultiplier,
            iterations = PerformanceMeasurement.MeasuredIterations,
            results = Results,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }
}
