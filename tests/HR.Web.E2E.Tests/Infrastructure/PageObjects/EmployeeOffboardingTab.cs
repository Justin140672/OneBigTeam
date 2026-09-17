using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the offboarding checklist section (EmployeeOffboardingTab.razor). SPEC-OFF-01
/// merged this into the unified "Leaving &amp; Offboarding" workspace as an embedded sub-section
/// (id="leaving-checklist-section" on the parent — see EmployeeLeavingTab.razor) rather than its
/// own profile tab, so <see cref="OpenAsync"/> now delegates to <see cref="EmployeeLeavingTab"/>'s
/// navigation instead of selecting a separate tab.
///
/// An offboarding plan is only ever created as a side effect of the "Start Leaving Process" wizard
/// (see StartLeavingProcessHandler, which calls IOffboardingPlanCoordinator.StartAsync internally)
/// — there is no direct manual trigger for it anywhere in the UI. By the time <see cref="OpenAsync"/>
/// is called, a plan is expected to already exist (created via <see cref="StartLeavingProcessDialog"/>).
/// </summary>
public sealed class EmployeeOffboardingTab(IPage page)
{
    /// <summary>
    /// Navigates to the unified "Leaving &amp; Offboarding" workspace and waits for the checklist
    /// section to render — either the progress panel's progress bar (a plan exists, the expected
    /// case) or the "Offboarding starts automatically..." empty state (still present in the source
    /// as a defensive fallback, even though no real UI flow reaches it once a leaving process
    /// exists).
    /// </summary>
    public async Task OpenAsync()
    {
        await EmployeeEditPage.NavigateToSectionAsync(page, "Leaving");
        await page.WaitForSelectorAsync(
            "#leaving-checklist-section .progress, #leaving-checklist-section .hr-empty-state",
            new() { Timeout = 15_000 });
    }

    private ILocator Section => page.Locator("#leaving-checklist-section");

    // ── Plan overview (progress panel + checklist) ───────────────────────────────

    /// <summary>Returns true if the offboarding progress panel (status badge + progress bar) is visible.</summary>
    public Task<bool> HasProgressPanelAsync() => Section.Locator(".progress").IsVisibleAsync();

    /// <summary>Returns true if the Offboarding Checklist card is visible.</summary>
    public Task<bool> HasChecklistCardAsync() =>
        Section.Locator(".card-header:has-text('Offboarding Checklist')").IsVisibleAsync();

    /// <summary>Returns the text of the offboarding plan status badge on the progress panel.</summary>
    public async Task<string?> GetStatusBadgeTextAsync()
    {
        var badge = Section.Locator(".card .badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the current offboarding progress percentage, read from the progress bar's
    /// aria-valuenow attribute (more robust than scraping the "NN%" caption text).
    /// </summary>
    public async Task<int> GetProgressPercentAsync()
    {
        var bar = Section.Locator(".progress .progress-bar");
        var value = await bar.GetAttributeAsync("aria-valuenow");
        return int.TryParse(value, out var percent) ? percent : 0;
    }

    private ILocator ChecklistCard => Section.Locator(".card").Filter(new() { HasText = "Offboarding Checklist" }).First;

    private ILocator RowFor(string taskTitleFragment) =>
        ChecklistCard.Locator("table tbody tr").Filter(new() { HasText = taskTitleFragment }).First;

    /// <summary>
    /// Returns the status badge text ("Pending"/"In Progress"/"Completed"/"Waived"/"Overdue"/…)
    /// for the checklist row whose Task cell contains <paramref name="taskTitleFragment"/>.
    /// </summary>
    public async Task<string?> GetChecklistTaskStatusAsync(string taskTitleFragment)
    {
        var badge = RowFor(taskTitleFragment).Locator(".badge");
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Returns true if any row in the Offboarding Checklist table's Task column contains <paramref name="taskTitleFragment"/>.</summary>
    public Task<bool> HasChecklistTaskAsync(string taskTitleFragment) =>
        RowFor(taskTitleFragment).WaitUntilVisibleAsync();

    /// <summary>Returns the Owner column text for the given checklist row.</summary>
    public async Task<string?> GetChecklistTaskOwnerAsync(string taskTitleFragment) =>
        (await RowFor(taskTitleFragment).Locator("td").Nth(2).TextContentAsync())?.Trim();

    /// <summary>Returns the Due Date column text for the given checklist row (e.g. "20 Oct 2026" or "—").</summary>
    public async Task<string?> GetChecklistTaskDueDateAsync(string taskTitleFragment) =>
        (await RowFor(taskTitleFragment).Locator("td").Nth(3).TextContentAsync())?.Trim();

    /// <summary>Returns the small "Waived: &lt;reason&gt;" caption text under a waived row's title, or null if not shown.</summary>
    public async Task<string?> GetChecklistTaskWaiveReasonAsync(string taskTitleFragment)
    {
        var caption = RowFor(taskTitleFragment).Locator(".text-muted.small").Filter(new() { HasText = "Waived:" }).First;
        return await caption.IsVisibleAsync() ? (await caption.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Returns true if the checklist row has an "Open task" link/button (OpenTaskId is set).</summary>
    public Task<bool> HasOpenTaskButtonAsync(string taskTitleFragment) =>
        RowFor(taskTitleFragment).GetByRole(AriaRole.Button, new() { Name = "Open task" }).IsVisibleAsync();

    /// <summary>Clicks the "Open task" button for the given checklist row, opening the TaskViewDialog.</summary>
    public Task ClickOpenTaskAsync(string taskTitleFragment) =>
        RowFor(taskTitleFragment).GetByRole(AriaRole.Button, new() { Name = "Open task" }).ClickAsync();

    /// <summary>Returns true if the checklist row has a "Waive" action button (CanWaive + LeavingInProgress + Pending/InProgress).</summary>
    public Task<bool> HasWaiveButtonAsync(string taskTitleFragment) =>
        RowFor(taskTitleFragment).GetByRole(AriaRole.Button, new() { Name = "Waive" }).IsVisibleAsync();

    /// <summary>
    /// Clicks the "Waive" action button for the given checklist row, opening
    /// <see cref="WaiveOffboardingTaskDialog"/>, and waits for the dialog to attach/animate in
    /// before returning — the Syncfusion dialog is not necessarily visible in the very next frame
    /// after the click, so callers checking <c>IsVisibleAsync</c> immediately need this to have
    /// already settled.
    /// </summary>
    public async Task ClickWaiveAsync(string taskTitleFragment)
    {
        await RowFor(taskTitleFragment).GetByRole(AriaRole.Button, new() { Name = "Waive" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Waive Obligation" })
            .WaitForAsync(new() { Timeout = 8_000 });
    }
}
