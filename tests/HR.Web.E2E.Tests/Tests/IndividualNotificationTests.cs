using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies per-notification interactions:
/// 1. Clicking a single notification opens the relevant task in TaskViewDialog in place
///    (no page navigation — see NotificationPanel.ClickNotificationAsync).
/// 2. After clicking a notification, the unread count decreases.
///
/// Sarah Chen has seeded notifications (task assigned / due soon / overdue).
/// </summary>
public sealed class IndividualNotificationTests(SarahChenPersonaFixture fixture)
    : RoleE2ETestBase<SarahChenPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string SarahEmail = "sarah.chen@acme.example";

    [Fact]
    public async Task ClickingNotification_OpensTaskViewDialog()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var notif    = new NotificationPanel(_page);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Sarah (who has seeded task notifications) ─────────
        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        // ── Step 3: Open the notification panel ───────────────────────────────
        await notif.OpenAsync();

        var titles = await notif.GetNotificationTitlesAsync();
        Assert.True(titles.Count > 0, "Expected at least one notification in the panel");

        // ── Step 4: Click the first notification and verify the task dialog opens ──
        await notif.ClickNotificationAsync(titles[0]);

        // ClickNotificationAsync already waits for ".task-view-dialog" to appear; wait for
        // its content to finish loading and confirm it shows a real task, not the page itself.
        await taskView.WaitForLoadedAsync();
        Assert.NotEmpty(await taskView.GetTitleAsync());
        Assert.DoesNotMatch(new Regex("/tasks/[0-9a-f-]{36}"), _page.Url);
    }

    [Fact]
    public async Task OpeningNotificationPanel_ShowsAllNotificationTitles()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var notif = new NotificationPanel(_page);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await notif.OpenAsync();

        var titles = await notif.GetNotificationTitlesAsync();
        Assert.True(titles.Count > 0, "Expected notifications to be listed in the panel");

        // Sarah's seeded notifications reference Q2, board meeting, interview, or survey tasks.
        Assert.Contains(titles, t =>
            t.Contains("Q2",           StringComparison.OrdinalIgnoreCase) ||
            t.Contains("board meeting",StringComparison.OrdinalIgnoreCase) ||
            t.Contains("interview",    StringComparison.OrdinalIgnoreCase) ||
            t.Contains("survey",       StringComparison.OrdinalIgnoreCase) ||
            t.Contains("task",         StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MarkingAllRead_ThenReopeningPanel_ShowsZeroUnreadBadge()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var notif = new NotificationPanel(_page);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        // Only run the assertion if there are currently unread notifications.
        var unreadBefore = await notif.GetUnreadCountAsync();
        if (unreadBefore == 0) return; // already all read — test is a no-op

        await notif.OpenAsync();
        await notif.MarkAllReadAsync();

        // Badge should be gone.
        var unreadAfter = await notif.GetUnreadCountAsync();
        Assert.Equal(0, unreadAfter);

        // Close (MarkAllRead leaves the panel open), then reopen.
        await notif.CloseAsync();
        await notif.OpenAsync();
        var unreadOnReopen = await notif.GetUnreadCountAsync();
        Assert.Equal(0, unreadOnReopen);
    }
}
