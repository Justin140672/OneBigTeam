using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Offboarding tab on the employee edit page
/// (EmployeeOffboardingTab.razor). Covers the resulting plan summary / checklist once a plan
/// exists.
///
/// An offboarding plan is now only ever created as a side effect of the "Start Leaving Process"
/// wizard (see StartLeavingProcessHandler, which calls IOffboardingPlanCoordinator.StartAsync
/// internally) — there is no longer any direct manual trigger for it anywhere in the UI. By the
/// time <see cref="OpenAsync"/> is called, a plan is expected to already exist (created via
/// <see cref="StartLeavingProcessDialog"/>), so the tab is already visible and simply needs
/// clicking.
///
/// Follows the standalone-tab-page-object pattern established by <see cref="ContactDetailsTab"/>
/// rather than being folded into <see cref="EmployeeEditPage"/>.
/// </summary>
public sealed class EmployeeOffboardingTab(IPage page)
{
    /// <summary>
    /// Clicks the (already visible) Offboarding tab and waits for its content to render. Assumes
    /// an offboarding plan already exists — offboarding is only ever started as a side effect of
    /// the Start Leaving Process wizard, so the tab is always visible by the time this is called.
    /// </summary>
    public async Task OpenAsync()
    {
        await EmployeeEditPage.NavigateToSectionAsync(page, "Offboarding");

        // Wait for the tab content to render — either the progress panel's progress bar (a plan
        // exists, the expected case) or the "No offboarding plan found for this employee" empty
        // state (still present in the source as a defensive fallback, even though no real UI flow
        // reaches it anymore).
        await page.WaitForSelectorAsync(".progress, .hr-empty-state", new() { Timeout = 15_000 });
    }

    // ── Plan overview (progress panel + checklist) ───────────────────────────────

    /// <summary>Returns true if the offboarding progress panel (status badge + progress bar) is visible.</summary>
    public async Task<bool> HasProgressPanelAsync() =>
        await page.Locator(".progress").IsVisibleAsync();

    /// <summary>Returns true if the Offboarding Checklist card is visible.</summary>
    public async Task<bool> HasChecklistCardAsync() =>
        await page.Locator(".card-header:has-text('Offboarding Checklist')").IsVisibleAsync();

    /// <summary>Returns the text of the offboarding plan status badge on the progress panel.</summary>
    public async Task<string?> GetStatusBadgeTextAsync()
    {
        var badge = page.Locator(".card .badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the current offboarding progress percentage, read from the progress bar's
    /// aria-valuenow attribute (more robust than scraping the "NN%" caption text).
    /// </summary>
    public async Task<int> GetProgressPercentAsync()
    {
        var bar = page.Locator(".progress .progress-bar");
        var value = await bar.GetAttributeAsync("aria-valuenow");
        return int.TryParse(value, out var percent) ? percent : 0;
    }

    /// <summary>
    /// Returns the status badge text ("Pending"/"In Progress"/"Completed"/"Skipped"/"Overdue")
    /// for the Offboarding Checklist row whose Task cell contains <paramref name="taskTitleFragment"/>.
    /// Scoped to the "Offboarding Checklist" card specifically, mirroring
    /// EmployeeEditPage.GetOnboardingChecklistTaskStatusAsync's scoping-by-card-then-by-row-then-by-badge
    /// technique.
    /// </summary>
    public async Task<string?> GetChecklistTaskStatusAsync(string taskTitleFragment)
    {
        var checklistCard = page.Locator(".card").Filter(new() { HasText = "Offboarding Checklist" }).First;
        var row = checklistCard.Locator("table tbody tr").Filter(new() { HasText = taskTitleFragment }).First;
        var badge = row.Locator(".badge");
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Returns true if any row in the Offboarding Checklist table's Task column contains <paramref name="taskTitleFragment"/>.</summary>
    public async Task<bool> HasChecklistTaskAsync(string taskTitleFragment)
    {
        var checklistCard = page.Locator(".card").Filter(new() { HasText = "Offboarding Checklist" }).First;
        return await checklistCard.Locator("table tbody tr")
            .Filter(new() { HasText = taskTitleFragment })
            .First
            .WaitUntilVisibleAsync();
    }
}
