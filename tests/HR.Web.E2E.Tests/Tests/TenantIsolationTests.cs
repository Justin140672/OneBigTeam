using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that a Company 1 (Acme) user cannot see tasks, notifications, or task detail
/// pages that belong to Company 2 (Beta Corp).
/// </summary>
public sealed class TenantIsolationTests(CrossUserFixture fixture) : CrossUserTenantAndMiscTestBase(fixture)
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

    [Fact]
    public async Task BetaCorpLeaveTask_IsInvisibleToAcmeManager_AndTaskUrlIsBlocked()
    {
        var reason = $"E2E-TENANT-{Guid.NewGuid():N}";

        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile     = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var aliceProfile = new MyProfilePage(_page, _fixture.WebBaseUrl);
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

        // ── Step 2: Login as Alice (Beta Corp manager) and open the task ──────
        // Uses Alice's own profile Tasks tab — the role-agnostic replacement for the old
        // dashboard "My Tasks" widget (MyTasksWidget.razor), which is dead code now.
        await login.SwitchAccountAsync(AliceEmail);

        await aliceProfile.GoToAsync(BetaCorpId, AliceId);
        await aliceProfile.OpenTasksTabAsync();
        var aliceTasks = await aliceProfile.GetTaskTitlesAsync();
        Assert.Contains(aliceTasks, t => t.Contains("Bob Taylor", StringComparison.OrdinalIgnoreCase)
                                      || t.Contains("leave", StringComparison.OrdinalIgnoreCase));

        // Open the task dialog — there is no longer a standalone task URL to capture
        // (TaskViewDialog opens in place), so the cross-tenant check below (step 6)
        // instead targets Bob's profile URL directly.
        await aliceProfile.ClickTaskAsync("Bob Taylor");
        await taskView.WaitForLoadedAsync();

        Assert.Contains("Bob Taylor", await taskView.GetTitleAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.True(await taskView.HasLeaveReviewPanelAsync());

        // ── Step 3: Login as James (Acme manager) ────────────────────────────
        await login.SwitchAccountAsync(JamesEmail);

        // ── Step 4: James's own task list must NOT contain Bob's task ─────────
        await profile.GoToAsync(AcmeId, JamesId);
        await profile.OpenTasksTabAsync();
        var jamesTasks = await profile.GetTaskTitlesAsync();
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

        // ── Step 6: James cannot navigate to Bob's profile URL either ─────────
        // There is no longer a standalone task detail URL to attack directly (TaskViewDialog
        // opens in place from the owning employee's own profile Tasks tab), so the remaining
        // cross-tenant attack surface is the profile URL itself.
        await _page.GotoAsync(
            $"{_fixture.WebBaseUrl}/companies/{BetaCorpId}/employees/{BobId}/profile");

        // The app should redirect away or show an error — the page must NOT remain on
        // Bob's BetaCorp profile URL. We check the URL rather than page content because
        // Bob's name also appears in the dev persona switcher which is always in the topbar.
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        var expectedBlockedPath = $"/companies/{BetaCorpId}/employees/{BobId}/profile";
        Assert.DoesNotContain(expectedBlockedPath, finalUrl, StringComparison.OrdinalIgnoreCase);
    }
}
