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
    public IBrowser  Browser    => _browser!;

    public async Task InitializeAsync()
    {
        // Ensure DevAuth is active in child processes launched by Aspire.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.HR_AppHost>();

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        // Strip trailing slash so page objects can safely append paths.
        WebBaseUrl = _app.GetEndpoint("web").ToString().TrimEnd('/');

        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser   != null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_app       != null) await _app.DisposeAsync();
    }
}
