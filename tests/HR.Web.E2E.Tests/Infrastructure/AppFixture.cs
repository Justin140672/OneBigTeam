using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public sealed class AppFixture : IAsyncLifetime
{
    private DistributedApplication? _app;
    private IPlaywright?            _playwright;
    private IBrowser?               _browser;

    public string    WebBaseUrl { get; private set; } = "";
    public string    MarketingBaseUrl { get; private set; } = "";
    public IBrowser  Browser    => _browser!;

    public async Task InitializeAsync()
    {
        // Kill any stale testhost processes from a previous run — they hold Aspire's
        // ports and cause "Service postgres should have valid address at this point".
        KillStaleTestHosts();

        // Ensure DevAuth is active in child processes launched by Aspire.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("E2E_TESTING", "true");

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.HR_AppHost>();

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        // Strip trailing slash so page objects can safely append paths.
        WebBaseUrl = _app.GetEndpoint("web", "http").ToString().TrimEnd('/');
        MarketingBaseUrl = _app.GetEndpoint("marketing", "http").ToString().TrimEnd('/');

        // Probe until the web app is actually serving requests.
        // StartAsync returns as soon as Aspire begins orchestrating — Postgres migrations
        // and seed data may still be running, so we wait for a real HTTP response.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync($"{WebBaseUrl}/login");
                if ((int)response.StatusCode < 500) break;
            }
            catch { /* app not up yet */ }
            await Task.Delay(1_000);
        }

        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless  = false,
            SlowMo    = 100, // ms pause between actions — makes the run watchable
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser   != null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_app       != null) await _app.DisposeAsync();
    }

    private static void KillStaleTestHosts()
    {
        // "testhost" holds Aspire's ports and causes "Service postgres should have valid address
        // at this point"; a leftover "HR.Web" from a previous run that crashed or was killed
        // mid-test (rather than disposed cleanly via DisposeAsync) can similarly hold the app's own
        // port, causing every page load in a fresh run to hang against a dead/orphaned instance
        // instead of the one this run just started.
        foreach (var processName in new[] { "testhost", "HR.Web" })
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
