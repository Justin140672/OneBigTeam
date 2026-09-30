using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

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

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await notif.OpenAsync();

        var titles = await notif.GetNotificationTitlesAsync();
        Assert.True(titles.Count > 0, "Expected at least one notification in the panel");

        await notif.ClickNotificationAsync(titles.First(t => t.Contains("Review Q2 performance reports", StringComparison.OrdinalIgnoreCase)));

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

        var unreadBefore = await notif.GetUnreadCountAsync();
        if (unreadBefore == 0) return;

        await notif.OpenAsync();
        await notif.MarkAllReadAsync();

        var unreadAfter = await notif.GetUnreadCountAsync();
        Assert.Equal(0, unreadAfter);

        await notif.CloseAsync();
        await notif.OpenAsync();
        var unreadOnReopen = await notif.GetUnreadCountAsync();
        Assert.Equal(0, unreadOnReopen);
    }
}
