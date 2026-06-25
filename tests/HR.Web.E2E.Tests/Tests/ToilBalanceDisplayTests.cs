using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that the TOIL balance card is visible on the admin employee leave tab
/// and that the admin leave tab renders all expected balance sections.
/// </summary>
[Collection("E2E")]
public sealed class ToilBalanceDisplayTests : IAsyncLifetime
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId   = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid SarahId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string SarahEmail = "sarah.chen@acme.example";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public ToilBalanceDisplayTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task AdminLeaveTab_ShowsAllBalanceSections_IncludingToil()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura (HR Administrator) ─────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Navigate to Tom's admin employee profile ──────────────────
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{TomId}");
        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });

        // ── Step 3: Open the Leave tab (admin view) ───────────────────────────
        await _page.GetByRole(AriaRole.Tab, new() { Name = "Leave" }).ClickAsync();
        await _page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });

        // ── Step 4: Verify the Current Balance card is visible ────────────────
        Assert.True(
            await _page.Locator(".card-header").Filter(new() { HasText = "Current Balance" }).IsVisibleAsync(),
            "Expected a 'Current Balance' section on the admin Leave tab");

        // ── Step 5: Verify TOIL balance card is visible ───────────────────────
        Assert.True(
            await _page.Locator(".card-header, .card").Filter(new() { HasText = "TOIL" }).First.IsVisibleAsync(),
            "Expected a TOIL balance section on the admin Leave tab");

        // ── Step 6: Verify the page content contains Annual Leave balance data ─
        var content = await _page.ContentAsync();
        Assert.Contains("Annual Leave", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminLeaveTab_ShowsPendingAndApprovedRequestSections()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura ─────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Navigate to Sarah's admin profile (she has seeded requests) ─
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{SarahId}");
        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });

        // ── Step 3: Open the admin Leave tab ─────────────────────────────────
        await _page.GetByRole(AriaRole.Tab, new() { Name = "Leave" }).ClickAsync();
        await _page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });

        var content = await _page.ContentAsync();

        // ── Step 4: Both request summary sections must be rendered ────────────
        Assert.True(
            await _page.Locator(".card-header").Filter(new() { HasText = "Pending" }).IsVisibleAsync()
            || content.Contains("Pending", StringComparison.OrdinalIgnoreCase),
            "Expected a Pending Requests section");

        Assert.True(
            await _page.Locator(".card-header").Filter(new() { HasText = "Approved" }).IsVisibleAsync()
            || content.Contains("Approved", StringComparison.OrdinalIgnoreCase),
            "Expected an Approved Requests section");
    }
}
