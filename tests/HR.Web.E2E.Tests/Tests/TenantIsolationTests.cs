using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that a Company 1 (Acme) user cannot see tasks, notifications, or task detail
/// pages that belong to Company 2 (Beta Corp).
/// </summary>
[Collection("E2E")]
public sealed class TenantIsolationTests : IAsyncLifetime
{
    // ── Company 1 — Acme Corporation ─────────────────────────────────────────
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid JamesId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private const string JamesEmail = "james.okafor@acme.example";

    // ── Company 2 — Beta Corp ────────────────────────────────────────────────
    private static readonly Guid BetaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid AliceId    = Guid.Parse("30000000-0000-0000-0000-000000000011");
    private static readonly Guid BobId      = Guid.Parse("30000000-0000-0000-0000-000000000012");
    private const string AliceEmail = "alice.morgan@betacorp.example";
    private const string BobEmail   = "bob.taylor@betacorp.example";

    private const string StartDate = "05/10/2026";
    private const string EndDate   = "09/10/2026";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public TenantIsolationTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task BetaCorpLeaveTask_IsInvisibleToAcmeManager_AndTaskUrlIsBlocked()
    {
        var reason = $"E2E-TENANT-{Guid.NewGuid():N}";

        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile     = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var aliceProfile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var dash        = new DashboardPage(_page, _fixture.WebBaseUrl);
        var notif       = new NotificationPanel(_page);
        var taskView    = new TaskViewPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Bob (Beta Corp employee) and submit leave ─────────
        await login.GoToAsync();
        await login.LoginAsync(BobEmail);

        await profile.GoToAsync(BetaCorpId, BobId);
        await profile.OpenLeaveTabAsync();
        await profile.ClickRequestLeaveAsync();
        await profile.FillLeaveRequestAsync("Annual Leave", StartDate, EndDate, reason);
        await profile.SubmitLeaveRequestAsync();

        await _page.WaitForSelectorAsync("table tbody tr", new() { Timeout = 15_000 });
        Assert.Equal("Pending", await profile.GetLeaveRequestStatusAsync(reason));

        // ── Step 2: Login as Alice (Beta Corp manager) and find the task URL ──
        await login.SwitchAccountAsync(AliceEmail);

        await dash.GoToAsync();
        var aliceTasks = await dash.GetTaskTitlesAsync();
        Assert.Contains(aliceTasks, t => t.Contains("Bob Taylor", StringComparison.OrdinalIgnoreCase)
                                      || t.Contains("leave", StringComparison.OrdinalIgnoreCase));

        // Navigate to the task so we can capture its ID.
        await dash.ClickTaskAsync("Bob Taylor");
        var betaTaskId  = taskView.GetTaskIdFromUrl();
        var betaTaskUrl = _page.Url;

        Assert.Contains("Bob Taylor", await taskView.GetTitleAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.True(await taskView.HasLeaveReviewPanelAsync());

        // ── Step 3: Login as James (Acme manager) ────────────────────────────
        await login.SwitchAccountAsync(JamesEmail);

        // ── Step 4: James's My Tasks widget must NOT contain Bob's task ───────
        await dash.GoToAsync();
        var jamesTasks = await dash.GetTaskTitlesAsync();
        Assert.DoesNotContain(jamesTasks,
            t => t.Contains("Bob Taylor", StringComparison.OrdinalIgnoreCase));

        // ── Step 5: Notification bell must NOT show Beta Corp notification ────
        var unread = await notif.GetUnreadCountAsync();

        // If there are any notifications, none should mention Bob Taylor.
        if (unread > 0)
        {
            await notif.OpenAsync();
            var titles = await notif.GetNotificationTitlesAsync();
            Assert.DoesNotContain(titles,
                t => t.Contains("Bob Taylor", StringComparison.OrdinalIgnoreCase));
            await notif.CloseAsync();
        }

        // ── Step 6: Navigating directly to Beta Corp's task URL is blocked ────
        // James is authenticated as Acme company_id; the task belongs to Beta Corp.
        await _page.GotoAsync(betaTaskUrl);
        await _page.WaitForSelectorAsync(".alert-danger, h1", new() { Timeout = 15_000 });

        Assert.True(await taskView.IsNotFoundAsync(),
            $"Expected 'Task not found' when Acme user navigates to Beta Corp task {betaTaskId}");

        // ── Step 7: James cannot navigate to Bob's profile URL either ─────────
        await _page.GotoAsync(
            $"{_fixture.WebBaseUrl}/companies/{BetaCorpId}/employees/{BobId}/profile");

        // The app should either redirect away or show an error — it must NOT render
        // Bob's personal profile data under James's Acme session.
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });
        var content = await _page.ContentAsync();
        Assert.DoesNotContain("Bob Taylor", content, StringComparison.OrdinalIgnoreCase);
    }
}
