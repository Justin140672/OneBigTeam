using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Leaving tab on the employee edit page (EmployeeLeavingTab.razor). Unlike
/// Onboarding (auto-created) or Offboarding (started from its own empty-state button), a leaving
/// process is only ever started via the Employee Overview header's "Start Leaving Process"
/// button, which opens <see cref="StartLeavingProcessDialog"/> rather than navigating into this
/// tab directly — the tab itself is hidden entirely until a process exists (see
/// EmployeeEdit.razor's _showLeavingTab / GetEmployeeResponse.ShowLeavingTab). While the process
/// is "InProgress", the card header also exposes "Amend" (<see cref="AmendLeavingProcessDialog"/>)
/// and "Cancel Leaving Process" (<see cref="CancelLeavingProcessDialog"/>) actions.
///
/// Follows the standalone-tab-page-object pattern established by <see cref="EmployeeOffboardingTab"/>.
/// </summary>
public sealed class EmployeeLeavingTab(IPage page)
{
    /// <summary>
    /// Opens the Leaving tab. Only valid once a leaving process has actually been started (the
    /// tab isn't rendered at all otherwise) — callers should assert
    /// <see cref="IsTabVisibleAsync"/> or use <see cref="HasStartLeavingProcessButtonAsync"/>
    /// beforehand if that isn't already guaranteed.
    /// </summary>
    public async Task OpenAsync()
    {
        await EmployeeEditPage.NavigateToSectionAsync(page, "Leaving");
        await page.WaitForSelectorAsync(".card-header:has-text('Leaving Details'), .hr-empty-state", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns true if the "Leaving" tab is present in the tab strip. The tab lives under the
    /// "Tasks &amp; Records" group, whose inner strip only renders once that group is selected —
    /// so open the group first, then check.
    /// </summary>
    public async Task<bool> IsTabVisibleAsync()
    {
        await page.Locator(".employee-profile-groups > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = "Tasks & Records", Exact = true })
            .ClickAsync();
        return await page.Locator(".employee-profile-sections > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = "Leaving", Exact = true })
            .IsVisibleAsync();
    }

    /// <summary>
    /// Returns true if the "More actions" overflow menu's "Start offboarding" item is present —
    /// only shown while no leaving process is active (see EmployeeEdit.razor's `!_showLeavingTab`
    /// guard / BuildMoreActionsItems). Replaces the old direct header "Start Leaving Process"
    /// button now that the action lives inside the "More actions" dropdown — see
    /// EmployeeEditPage.HasStartOffboardingMenuItemAsync, which this delegates to.
    /// </summary>
    public Task<bool> HasStartLeavingProcessButtonAsync() =>
        new EmployeeEditPage(page, string.Empty).HasStartOffboardingMenuItemAsync();

    /// <summary>Returns true if the "No leaving process found for this employee" empty state is visible.</summary>
    public Task<bool> IsEmptyStateVisibleAsync() =>
        page.Locator(".hr-empty-state").IsVisibleAsync();

    private ILocator DetailsCard =>
        page.Locator(".card").Filter(new() { HasText = "Leaving Details" }).First;

    public async Task<string?> GetResignationReceivedDateTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(0).TextContentAsync())?.Trim();

    public async Task<string?> GetLeavingDateTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(1).TextContentAsync())?.Trim();

    public async Task<string?> GetLastWorkingDayTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(2).TextContentAsync())?.Trim();

    public async Task<string?> GetNoticePeriodTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(3).TextContentAsync())?.Trim();

    public async Task<string?> GetNoticeSourceTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(4).TextContentAsync())?.Trim();

    public async Task<string?> GetLeavingReasonTextAsync() =>
        (await DetailsCard.Locator("dl dd").Nth(5).TextContentAsync())?.Trim();

    /// <summary>Returns the text of the leaving process status badge ("In Progress"/"Cancelled"/"Completed").</summary>
    public async Task<string?> GetStatusBadgeTextAsync()
    {
        var badge = DetailsCard.Locator(".badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns true if the "Amend" button is visible in the Leaving Details card header — only
    /// shown while the leaving process's Status is "InProgress".
    /// </summary>
    public Task<bool> HasAmendButtonAsync() =>
        DetailsCard.GetByRole(AriaRole.Button, new() { Name = "Amend", Exact = true }).IsVisibleAsync();

    /// <summary>
    /// Returns true if the "Cancel Leaving Process" button is visible in the Leaving Details card
    /// header — only shown while the leaving process's Status is "InProgress".
    /// </summary>
    public Task<bool> HasCancelButtonAsync() =>
        DetailsCard.GetByRole(AriaRole.Button, new() { Name = "Cancel Leaving Process", Exact = true }).IsVisibleAsync();

    /// <summary>
    /// Returns true if the persistent "Offboarding has already started for this employee — review
    /// the Offboarding tab for outstanding tasks." banner is visible above the Leaving Details
    /// card. Only rendered when the "offboardingAlreadyStarted=true" query-string flag is present
    /// — set by EmployeeEdit.razor's OnLeavingProcessAmended after a successful Amend whose
    /// response reported AmendLeavingProcessResponse.OffboardingAlreadyStarted.
    /// </summary>
    public Task<bool> HasOffboardingAlreadyStartedWarningAsync() =>
        page.Locator(".alert-warning")
            .Filter(new() { HasText = "Offboarding has already started for this employee" })
            .IsVisibleAsync();
}
