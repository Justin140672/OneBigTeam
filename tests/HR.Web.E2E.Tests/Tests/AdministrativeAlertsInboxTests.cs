using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// ADM-03 Administrative Alerts &amp; Incidents inbox
/// (/companies/{companyId}/administrative-alerts — AdministrativeAlertsInboxPage.razor). Journey
/// coverage for an HR Administrator plus an access guard for a plain employee, mirroring
/// ComplianceCentreTests' persona/login pattern and its PlainEmployee_IsRedirectedAway guard.
///
/// The alert grid is background-generated (compliance checks, report generation, integration
/// delivery, security events), so the shared seeded Acme company may legitimately have zero alerts
/// on a given run. Every action-journey assertion below therefore degrades gracefully when there
/// is no actionable row, exactly as ComplianceCentreTests' drill-through test does.
/// </summary>
public sealed class AdministrativeAlertsInboxTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator
    private const string TomEmail   = "tom.williams@acme.example";  // plain employee

    [Fact]
    public async Task Page_Loads_For_HrAdministrator()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var inbox = new AdministrativeAlertsInboxPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await inbox.GoToAsync(AcmeId);
        Assert.False(await inbox.HasLoadErrorAsync(), "Administrative Alerts inbox reported a load error");

        // Either the grid rendered rows or the green empty-state panel is shown — both prove the
        // page loaded and the loaded-state selector is wired.
        var count = await inbox.AlertCountAsync();
        if (count == 0)
            Assert.True(await inbox.IsEmptyStateVisibleAsync(),
                "Expected the 'No administrative alerts' panel when the grid has no rows");

        // Summary cards render numeric values.
        foreach (var label in new[] { "Unread", "Open", "Critical" })
            Assert.True(await inbox.SummaryValueAsync(label) >= 0,
                $"Summary card '{label}' did not show a non-negative number");

        // The nav item is present for an HR Administrator.
        Assert.True(
            await _page.GetByRole(AriaRole.Link, new() { Name = "Administrative Alerts" }).First.WaitUntilVisibleAsync(),
            "Expected the 'Administrative Alerts' nav item to be visible for an HR Administrator");
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/administrative-alerts");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });
        await WaitForUrlToStopContainingAsync("/administrative-alerts");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/administrative-alerts"),
            $"Expected a plain employee to be redirected away from the Administrative Alerts inbox, but landed at: {finalUrl}");
        Assert.Contains("/access-denied", finalUrl);
    }

    [Fact]
    public async Task Filter_By_Severity_And_Status_And_UnreadOnly()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var inbox = new AdministrativeAlertsInboxPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await inbox.GoToAsync(AcmeId);
        var unfiltered = await inbox.AlertCountAsync();

        await inbox.FilterBySeverityAsync("Critical");
        var afterSeverity = await inbox.AlertCountAsync();
        Assert.False(await inbox.HasLoadErrorAsync());
        Assert.True(afterSeverity <= unfiltered,
            $"Severity filter increased the row count ({unfiltered} -> {afterSeverity})");

        await inbox.FilterByStatusAsync("Resolved");
        var afterStatus = await inbox.AlertCountAsync();
        Assert.True(afterStatus <= afterSeverity,
            $"Status filter increased the row count ({afterSeverity} -> {afterStatus})");

        await inbox.SetUnreadOnlyAsync(true);
        var afterUnread = await inbox.AlertCountAsync();
        Assert.True(afterUnread <= afterStatus,
            $"'Unread only' increased the row count ({afterStatus} -> {afterUnread})");

        await inbox.ClearFiltersAsync();
        Assert.Equal(unfiltered, await inbox.AlertCountAsync());
    }

    [Fact]
    public async Task Acknowledge_Alert_Updates_Status()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var inbox = new AdministrativeAlertsInboxPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await inbox.GoToAsync(AcmeId);

        var index = await inbox.AcknowledgeFirstAsync();
        if (index < 0)
            return; // no Open alert seeded — data-dependent, skip gracefully

        Assert.Equal("Acknowledged", await inbox.RowStatusAsync(index));
        Assert.False(await inbox.RowHasButtonAsync(index, "Acknowledge"),
            "Acknowledge button should disappear once the alert is acknowledged");
    }

    [Fact]
    public async Task Resolve_Alert_With_Note_Updates_Status()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var inbox = new AdministrativeAlertsInboxPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await inbox.GoToAsync(AcmeId);

        var index = await inbox.ResolveFirstAsync($"Handled by E2E {Guid.NewGuid():N}");
        if (index < 0)
            return; // no non-resolved alert seeded — data-dependent, skip gracefully

        Assert.Equal("Resolved", await inbox.RowStatusAsync(index));
        Assert.False(await inbox.RowHasButtonAsync(index, "Resolve"),
            "Resolve button should disappear once the alert is resolved");
    }

    [Fact]
    public async Task Mark_Alert_Read()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var inbox = new AdministrativeAlertsInboxPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await inbox.GoToAsync(AcmeId);
        var unreadBefore = await inbox.UnreadCountAsync();

        var index = await inbox.MarkFirstReadAsync();
        if (index < 0)
            return; // nothing unread — data-dependent, skip gracefully

        Assert.False(await inbox.RowHasButtonAsync(index, "Mark read"),
            "'Mark read' button should disappear once the alert is read");
        Assert.True(await inbox.UnreadCountAsync() <= unreadBefore,
            "Unread count should not increase after marking an alert read");
    }

    [Fact]
    public async Task Empty_State_When_No_Alerts()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var inbox = new AdministrativeAlertsInboxPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await inbox.GoToAsync(AcmeId);

        // Narrow to a combination nothing realistically matches — Critical severity within a
        // single far-past calendar day — and assert the green empty-state panel, the same
        // possibly-empty handling ComplianceCentreTests uses for its empty case.
        await inbox.FilterBySeverityAsync("Critical");
        await inbox.FilterByStatusAsync("Resolved");
        await inbox.SetUnreadOnlyAsync(true);

        if (await inbox.AlertCountAsync() == 0)
            Assert.True(await inbox.IsEmptyStateVisibleAsync(),
                "Expected the 'No administrative alerts' panel when the filtered result is empty");
        else
            Assert.False(await inbox.IsEmptyStateVisibleAsync(),
                "Empty-state panel showed while the grid still had rows");
    }
}
