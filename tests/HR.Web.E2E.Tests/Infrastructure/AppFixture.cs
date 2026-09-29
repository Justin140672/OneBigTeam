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
        KillStaleTestHosts();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("E2E_TESTING", "true");

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.HR_AppHost>();

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        WebBaseUrl = _app.GetEndpoint("web", "http").ToString().TrimEnd('/');
        MarketingBaseUrl = _app.GetEndpoint("marketing", "http").ToString().TrimEnd('/');
        ApiBaseUrl = _app.GetEndpoint("api", "http").ToString().TrimEnd('/');
        AdminWebBaseUrl = _app.GetEndpoint("adminweb", "http").ToString().TrimEnd('/');

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow.AddMinutes(5);

        await WaitUntilReadyAsync(http, $"{ApiBaseUrl}/health/ready", deadline);

        await WaitUntilRespondingAsync(http, $"{WebBaseUrl}/login", deadline);
        await WaitUntilRespondingAsync(http, $"{MarketingBaseUrl}/", deadline);
        await WaitUntilRespondingAsync(http, $"{AdminWebBaseUrl}/login", deadline);

        await WaitUntilReadyAsync(http, $"{WebBaseUrl}/health/ready", deadline);

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
