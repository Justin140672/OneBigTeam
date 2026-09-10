using System.Net;
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
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow.AddMinutes(5);

        // Probe HR.Api directly FIRST. Every persona login drives a HR.Web -> HR.Api round-trip
        // (POST /api/login etc.); on a slow CI host HR.Web can be serving /login while HR.Api is
        // still applying migrations/seed, so the first login test hits a timeout that HR.Web's
        // catch-all reports as "Something went wrong." /health/ready is mapped in every environment
        // and returns 503 until startup migrations complete, so this waits for the real dependency.
        await WaitUntilReadyAsync(http, $"{ApiBaseUrl}/health/ready", deadline);

        await WaitUntilRespondingAsync(http, $"{WebBaseUrl}/login", deadline);
        await WaitUntilRespondingAsync(http, $"{MarketingBaseUrl}/", deadline);
        await WaitUntilRespondingAsync(http, $"{AdminWebBaseUrl}/login", deadline);

        // Final gate: confirm the actual failing path — a HR.Web request that fans out to HR.Api —
        // works before releasing the suite, not just that HR.Web serves its own static /login.
        // HR.Web's /health/ready aggregates its checks; a plain 200 here plus the direct API
        // readiness above means both ends and the network between them are live.
        await WaitUntilReadyAsync(http, $"{WebBaseUrl}/health/ready", deadline);

        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            // Headless=true is a hard requirement, not a preference — this suite runs on a build
            // server with no display, so Headless=false is not an option there (it's only ever
            // useful as a local, throwaway diagnostic on a machine that HAS a display). A live
            // headless run has previously spiked from ~17 failures to 145+, overwhelmingly
            // Syncfusion combobox/dialog timing races (see memory: E2E headless combobox
            // flakiness) — the throttling args below help but are not sufficient on their own.
            // Fix headless reliability at its source (harden the actual page-object interactions —
            // e.g. DropDownSelector's retry loops — the same way the Kanban drag flakiness was
            // eventually fixed) rather than flipping this to Headless=false; that "fix" just moves
            // the failures to whichever environment can't use it.
            // Local diagnostic only: E2E_HEADED=1 to watch a run (with a slow-mo delay). NEVER commit
            // a headed/slow-mo default — the suite's reliability contract is headless at
            // maxParallelThreads=15, and the build server has no display.
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

    // Like WaitUntilRespondingAsync but requires a genuine 2xx (readiness), not just "not 5xx".
    // /health/ready returns 503 while startup migrations/seed are still running, so a <500 check
    // would release the suite too early. Throws on deadline so a stuck dependency fails loudly and
    // deterministically instead of letting every login test flake.
    private static async Task WaitUntilReadyAsync(HttpClient http, string url, DateTime deadline)
    {
        HttpStatusCode? lastStatus = null;
        string? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync(url);
                lastStatus = response.StatusCode;
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception ex) { lastError = ex.Message; }
            await Task.Delay(1_000);
        }

        throw new TimeoutException(
            $"Readiness probe never succeeded for {url} within the startup deadline "
            + $"(last status: {lastStatus?.ToString() ?? "none"}, last error: {lastError ?? "none"}).");
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
