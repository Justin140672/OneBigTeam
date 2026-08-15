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
    public string    ApiBaseUrl { get; private set; } = "";
    public string    AdminWebBaseUrl { get; private set; } = "";
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
        ApiBaseUrl = _app.GetEndpoint("api", "http").ToString().TrimEnd('/');
        // "adminweb" — HR.Admin.Web, the internal Admin Portal (Customer Dashboard epic). See
        // AppHost.cs: registered with the same E2E-pinned "http" launch profile as web/api/marketing.
        AdminWebBaseUrl = _app.GetEndpoint("adminweb", "http").ToString().TrimEnd('/');

        // Probe until every app the tests navigate to directly is actually serving requests.
        // StartAsync returns as soon as Aspire begins orchestrating — Postgres migrations
        // and seed data may still be running, so we wait for a real HTTP response. AppHost.cs only
        // makes "marketing" WaitFor("api"), not "web" — marketing and web start in parallel, so
        // marketing can still be mid-startup even once web already answers /login. Without probing
        // it separately here too, a test that's first to navigate to MarketingBaseUrl in a run can
        // hit that startup window as ERR_CONNECTION_REFUSED instead of a clean wait.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddMinutes(3);
        await WaitUntilRespondingAsync(http, $"{WebBaseUrl}/login", deadline);
        await WaitUntilRespondingAsync(http, $"{MarketingBaseUrl}/", deadline);
        await WaitUntilRespondingAsync(http, $"{AdminWebBaseUrl}/login", deadline);

        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            //Headless = true,
            Headless = false,
            SlowMo = 100, // ms pause between actions — makes the run watchable

            // Headless Chromium treats every page as backgrounded/unfocused (there's no real
            // window to hold focus), which triggers Chrome's normal power-saving throttling of
            // background-tab timers — setTimeout/rAF-based work can be delayed to as little as
            // once per second. Headed runs don't hit this because the page is a real, focused
            // window. Syncfusion's AllowFiltering debounce (and other internal JS timers) rely on
            // exactly this kind of timer, so filtering/popup state that updates promptly in a
            // headed run can stall for seconds in headless — surfacing as the combobox item-list
            // waits in DropDownSelector timing out even though nothing is actually broken. These
            // flags disable that throttling so headless behaves like a normal focused tab.
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
        if (_browser   != null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_app       != null) await _app.DisposeAsync();
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
        // "testhost" holds Aspire's ports and causes "Service postgres should have valid address
        // at this point"; a leftover "HR.Web"/"HR.Api"/"HR.Marketing" from a previous run that
        // crashed or was killed mid-test (rather than disposed cleanly via DisposeAsync) can
        // similarly hold that project's own fixed E2E port (AppHost.cs pins the "http" launch
        // profile rather than Aspire's dynamic allocation when E2E_TESTING=true) — this run's
        // app.GetEndpoint still resolves to the expected URL, but the server actually listening on
        // it is the dead stale process, not the one this run just started, surfacing as
        // ERR_CONNECTION_REFUSED (or a hang) on navigation instead of a clear "port in use" startup
        // failure.
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
