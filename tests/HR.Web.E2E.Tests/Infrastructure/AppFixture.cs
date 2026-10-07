using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public sealed class AppFixture : IAsyncLifetime
{
    private const int LogLinesPerResourceFile = 200;
    private const int LogLinesPerResourceConsole = 30;
    private static readonly TimeSpan DiagnosticWatchWindow = TimeSpan.FromSeconds(3);

    private readonly BufferingLoggerProvider _appHostLog = new();
    private IDistributedApplicationTestingBuilder? _builder;
    private DistributedApplication? _app;
    private IPlaywright?            _playwright;
    private IBrowser?               _browser;
    private int                     _disposed;

    public string    WebBaseUrl { get; private set; } = "";
    public string    MarketingBaseUrl { get; private set; } = "";
    public string    ApiBaseUrl { get; private set; } = "";
    public string    AdminWebBaseUrl { get; private set; } = "";
    public IBrowser  Browser    => _browser!;

    public async Task InitializeAsync()
    {
        KillStaleTestHosts();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("E2E_TESTING", "true");

        _builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.HR_AppHost>();
        // Aspire mirrors every child-resource console line through HR.AppHost.Resources.*.
        // Keep warnings/errors in the CI console; full resource tails are captured explicitly
        // by CaptureStartupDiagnosticsAsync when startup fails.
        _builder.Services.AddLogging(logging =>
            logging.AddFilter("HR.AppHost.Resources", LogLevel.Warning));
        _builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(_appHostLog);

        _app = await _builder.BuildAsync();
        await _app.StartAsync();

        WebBaseUrl = _app.GetEndpoint("web", "http").ToString().TrimEnd('/');
        MarketingBaseUrl = _app.GetEndpoint("marketing", "http").ToString().TrimEnd('/');
        ApiBaseUrl = _app.GetEndpoint("api", "http").ToString().TrimEnd('/');
        AdminWebBaseUrl = _app.GetEndpoint("adminweb", "http").ToString().TrimEnd('/');

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow.AddMinutes(5);
        var probe = new ReadinessProbe(http, E2eDiag.Log, E2eDiag.AppendFile);

        await probe.WaitUntilReadyAsync(
            $"{ApiBaseUrl}/health/ready", deadline, $"{ApiBaseUrl}/health/startup-migrations");

        await WaitUntilRespondingAsync(http, $"{WebBaseUrl}/login", deadline);
        await WaitUntilRespondingAsync(http, $"{MarketingBaseUrl}/", deadline);
        await WaitUntilRespondingAsync(http, $"{AdminWebBaseUrl}/login", deadline);

        await probe.WaitUntilReadyAsync($"{WebBaseUrl}/health/ready", deadline);

        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !string.Equals(
                Environment.GetEnvironmentVariable("E2E_HEADED"), "1", StringComparison.Ordinal),
            SlowMo = string.Equals(
                Environment.GetEnvironmentVariable("E2E_HEADED"), "1", StringComparison.Ordinal) ? 250 : 0,
            Args =
            [
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-renderer-backgrounding",
            ],
        });
    }

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        await TryAsync("browser", async () => { if (_browser != null) await _browser.DisposeAsync(); });
        await TryAsync("playwright", () => { _playwright?.Dispose(); return Task.CompletedTask; });
        await TryAsync("aspire stop", async () =>
        {
            if (_app == null) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await _app.StopAsync(cts.Token);
        });
        await TryAsync("aspire dispose", async () => { if (_app != null) await _app.DisposeAsync(); });
        await TryAsync("aspire builder dispose", async () => { if (_builder != null) await _builder.DisposeAsync(); });
    }

    /// <summary>
    /// Emits Aspire resource state and recent logs (console + files under the diag directory). Must be
    /// called before <see cref="DisposeAsync"/> so the resource logs are still available. Never throws.
    /// </summary>
    public async Task CaptureStartupDiagnosticsAsync(Exception failure)
    {
        E2eDiag.Log("StartupDiag", $"startup failed: {failure.GetType().Name}: {DiagnosticText.SingleLine(DiagnosticText.Sanitize(failure.Message, 4000))}");
        E2eDiag.WriteFile("startup-failure.txt", DiagnosticText.Sanitize(failure.ToString(), 16_000));

        await TryAsync("apphost log", () =>
        {
            var lines = _appHostLog.Snapshot();
            E2eDiag.WriteFile("aspire/apphost.log", string.Join(Environment.NewLine, lines));
            foreach (var line in lines.TakeLast(LogLinesPerResourceConsole))
                E2eDiag.Log("StartupDiag", $"[apphost] {line}");
            return Task.CompletedTask;
        });

        var app = _app;
        if (app is null)
        {
            E2eDiag.Log("StartupDiag", "distributed app was never built; no resource state available");
            return;
        }

        await TryAsync("resource diagnostics", async () =>
        {
            var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
            var logger = app.Services.GetRequiredService<ResourceLoggerService>();

            var latest = new Dictionary<string, ResourceEvent>();
            using (var cts = new CancellationTokenSource(DiagnosticWatchWindow))
            {
                try
                {
                    await foreach (var resourceEvent in notifications.WatchAsync(cts.Token))
                        latest[resourceEvent.ResourceId] = resourceEvent;
                }
                catch (OperationCanceledException)
                {
                }
            }

            var summary = new StringBuilder();
            foreach (var (id, resourceEvent) in latest.OrderBy(e => e.Key))
            {
                var line = DescribeResource(id, resourceEvent);
                summary.AppendLine(line);
                E2eDiag.Log("StartupDiag", line);
            }

            E2eDiag.WriteFile("aspire/resource-states.txt", summary.ToString());

            await Task.WhenAll(latest.Keys.Select(id => CaptureResourceLogAsync(logger, id)));
        });
    }

    private static string DescribeResource(string id, ResourceEvent resourceEvent)
    {
        var snapshot = resourceEvent.Snapshot;
        var health = string.Join(
            "; ",
            snapshot.HealthReports.Select(h =>
                $"{h.Name}={h.Status?.ToString() ?? "pending"}"
                + (string.IsNullOrWhiteSpace(h.Description) ? "" : $" ({DiagnosticText.Sanitize(h.Description, 200)})")));

        return $"resource={id} state={snapshot.State?.Text ?? "unknown"} exitCode={snapshot.ExitCode?.ToString() ?? "none"} "
            + $"started={snapshot.StartTimeStamp?.ToString("o") ?? "never"} stopped={snapshot.StopTimeStamp?.ToString("o") ?? "no"} "
            + $"health=[{(health.Length == 0 ? "none" : health)}]";
    }

    private static async Task CaptureResourceLogAsync(ResourceLoggerService logger, string resourceId)
    {
        var lines = new List<string>();
        try
        {
            using var cts = new CancellationTokenSource(DiagnosticWatchWindow);
            try
            {
                await foreach (var batch in logger.WatchAsync(resourceId).WithCancellation(cts.Token))
                    lines.AddRange(batch.Select(l => DiagnosticText.Sanitize(l.Content, 1000)));
            }
            catch (OperationCanceledException)
            {
            }
        }
        catch (Exception ex)
        {
            E2eDiag.Log("StartupDiag", $"log capture for {resourceId} failed: {ex.GetType().Name}");
        }

        var tail = lines.TakeLast(LogLinesPerResourceFile).ToList();
        E2eDiag.WriteFile($"aspire/resource-logs/{resourceId}.log", string.Join(Environment.NewLine, tail));
        foreach (var line in tail.TakeLast(LogLinesPerResourceConsole))
            E2eDiag.Log("StartupDiag", $"[{resourceId}] {line}");
    }

    private static async Task TryAsync(string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            E2eDiag.Log("StartupDiag", $"{step} failed: {ex.GetType().Name}: {DiagnosticText.Sanitize(ex.Message, 300)}");
        }
    }

    private static async Task WaitUntilRespondingAsync(HttpClient http, string url, DateTime deadline)
    {
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync(url);
                if ((int)response.StatusCode < 500) return;
            }
            catch { /* app not up yet */ }
            await Task.Delay(1_000);
        }
    }

    private static void KillStaleTestHosts()
    {
        foreach (var processName in new[] { "testhost", "HR.Web", "HR.Api", "HR.Marketing", "HR.Admin.Web" })
        {
            try
            {
                var current = System.Diagnostics.Process.GetCurrentProcess();
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(processName))
                {
                    if (p.Id == current.Id) continue;
                    try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                }
            }
            catch { /* best-effort */ }
        }
    }
}
