using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that the Overview tab on the self-service My Profile page
/// renders the employee's employment details and action buttons.
/// </summary>
[Collection("E2E")]
public sealed class ProfileOverviewTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task OverviewTab_ShowsEmployeeJobTitle_AndActionButtons()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var overview = new OverviewTab(_page);

        // ── Step 1: Login as Tom ──────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // ── Step 2: Navigate to Tom's self-service profile ────────────────────
        await profile.GoToAsync(AcmeId, TomId);

        // ── Step 3: The Overview tab should be the default tab ────────────────
        // If it is not default, open it explicitly.
        if (!await overview.IsVisibleAsync())
            await profile.OpenOverviewTabAsync();

        await overview.WaitForLoadAsync();

        // ── Step 4: Overview grid is rendered ────────────────────────────────
        Assert.True(await overview.IsVisibleAsync(),
            "Expected the overview-grid to be visible on the Overview tab");

        // ── Step 5: Key employment details are displayed ──────────────────────
        var jobTitle = await overview.GetDetailAsync("Job Title");
        Assert.False(string.IsNullOrWhiteSpace(jobTitle),
            "Expected a Job Title to be displayed in the overview");
        Assert.Contains("Software Engineer", jobTitle, StringComparison.OrdinalIgnoreCase);

        var department = await overview.GetDetailAsync("Department");
        Assert.False(string.IsNullOrWhiteSpace(department),
            "Expected a Department to be displayed in the overview");
        Assert.Contains("Engineering", department, StringComparison.OrdinalIgnoreCase);

        // ── Step 6: Action buttons are present ───────────────────────────────
        var content = await _page.ContentAsync();
        Assert.Contains("Request Leave", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OverviewTab_RequestLeaveButton_OpensLeaveDialog()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var overview = new OverviewTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);

        if (!await overview.IsVisibleAsync())
            await profile.OpenOverviewTabAsync();

        await overview.WaitForLoadAsync();

        // ── Clicking "Request Leave" from the overview opens the leave dialog ─
        await overview.ClickRequestLeaveAsync();
        await _page.WaitForSelectorAsync(".e-dialog", new() { Timeout = 10_000 });

        Assert.True(await _page.Locator(".e-dialog").IsVisibleAsync(),
            "Expected the leave request dialog to open after clicking 'Request Leave' from the Overview tab");
    }
}
