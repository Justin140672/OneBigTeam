using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class EmployeeLeavingTab(IPage page)
{
    public async Task OpenAsync()
    {
        await EmployeeEditPage.NavigateToSectionAsync(page, "Leaving");
        await page.WaitForSelectorAsync("#leaving-details-section, .hr-empty-state", new() { Timeout = 15_000 });
    }

    public async Task<bool> IsTabVisibleAsync()
    {
        await page.Locator(".employee-profile-groups > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = "Tasks & Records", Exact = true })
            .ClickAsync();
        return await page.Locator(".employee-profile-sections > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = "Leaving & Offboarding", Exact = true })
            .IsVisibleAsync();
    }

    public Task<bool> HasStartLeavingProcessButtonAsync() =>
        new EmployeeEditPage(page, string.Empty).HasStartOffboardingMenuItemAsync();

    public Task<bool> IsEmptyStateVisibleAsync() =>
        page.Locator(".hr-empty-state").IsVisibleAsync();

    private ILocator DetailsSection => page.Locator("#leaving-details-section");

    private ILocator DetailsCard => DetailsSection;

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

    public async Task<string?> GetNotesTextAsync()
    {
        var dt = DetailsCard.Locator("dl dt").Filter(new() { HasText = "Notes" }).First;
        if (!await dt.IsVisibleAsync())
            return null;
        return (await dt.Locator("xpath=following-sibling::dd[1]").TextContentAsync())?.Trim();
    }

    public async Task<string?> GetCancellationReasonTextAsync()
    {
        var dt = DetailsCard.Locator("dl dt").Filter(new() { HasText = "Cancellation Reason" }).First;
        if (!await dt.IsVisibleAsync())
            return null;
        return (await dt.Locator("xpath=following-sibling::dd[1]").TextContentAsync())?.Trim();
    }

    public async Task<string?> GetStatusBadgeTextAsync()
    {
        var badge = DetailsSection.Locator(".card-header .badge").Nth(1);
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    public async Task<string?> GetOffboardingStatusBadgeTextAsync()
    {
        var badge = DetailsSection.Locator(".card-header .badge").Nth(2);
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    public Task<bool> HasAmendButtonAsync() =>
        DetailsSection.GetByRole(AriaRole.Button, new() { Name = "Amend", Exact = true }).IsVisibleAsync();

    public Task<bool> HasCancelButtonAsync() =>
        DetailsSection.GetByRole(AriaRole.Button, new() { Name = "Cancel Leaving Process", Exact = true }).IsVisibleAsync();

    public Task<bool> HasOffboardingAlreadyStartedWarningAsync() =>
        page.Locator(".alert-warning")
            .Filter(new() { HasText = "Offboarding has already started for this employee" })
            .IsVisibleAsync();


    public ILocator ChecklistSection => page.Locator("#leaving-checklist-section");

    public EmployeeOffboardingTab Checklist => new(page);


    private ILocator HistorySection => page.Locator("#leaving-history-section");

    public Task<bool> IsHistorySectionVisibleAsync() =>
        HistorySection.IsVisibleAsync();

    public async Task ExpandHistorySectionAsync()
    {
        var button = HistorySection.GetByRole(AriaRole.Button);
        var isExpanded = await button.Locator("i.fa-chevron-down").IsVisibleAsync();
        if (!isExpanded)
        {
            await button.ClickAsync();
            await HistorySection.Locator("table tbody").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
    }

    public async Task CollapseHistorySectionAsync()
    {
        var button = HistorySection.GetByRole(AriaRole.Button);
        var isCollapsed = await button.Locator("i.fa-chevron-right").IsVisibleAsync();
        if (!isCollapsed)
        {
            await button.ClickAsync();
            await HistorySection.Locator("table tbody").WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        }
    }

    public async Task<int> GetHistoryEntryCountAsync()
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return await rows.CountAsync();
    }

    public async Task<IReadOnlyList<string>> GetHistoryStatusesAsync()
    {
        var badges = HistorySection.Locator("table tbody td .badge");
        var count = await badges.CountAsync();
        var statuses = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var text = (await badges.Nth(i).TextContentAsync())?.Trim();
            if (!string.IsNullOrEmpty(text))
                statuses.Add(text);
        }
        return statuses;
    }

    public async Task<string?> GetHistoryLeavingReasonAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(4).TextContentAsync())?.Trim();
    }

    public async Task<string?> GetHistoryLeavingDateAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(2).TextContentAsync())?.Trim();
    }

    public async Task<string?> GetHistoryLastWorkingDayAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(3).TextContentAsync())?.Trim();
    }

    public async Task<string?> GetHistoryResignationDateAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(1).TextContentAsync())?.Trim();
    }

    public async Task<string?> GetHistoryReplacementManagerAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(7).TextContentAsync())?.Trim();
    }

    public async Task<string?> GetHistoryStartedDateAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(5).TextContentAsync())?.Trim();
    }

    public async Task<string?> GetHistoryEndedDateAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        return (await rows.Nth(rowIndex).Locator("td").Nth(6).TextContentAsync())?.Trim();
    }

    public async Task<string?> GetHistoryNotesAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        var nextRowLocator = rows.Nth(rowIndex).Locator("xpath=following-sibling::tr[1]");

        if (!await nextRowLocator.IsVisibleAsync())
            return null;

        var text = await nextRowLocator.Locator("td").TextContentAsync();
        if (text?.Contains("Notes:") == true)
        {
            var notesText = text.Replace("Notes:", "").Trim();
            return string.IsNullOrEmpty(notesText) ? null : notesText;
        }

        return null;
    }

    public async Task<string?> GetHistoryCancellationReasonAsync(int rowIndex)
    {
        var rows = HistorySection.Locator("table tbody tr").Filter(new() { Has = page.Locator("td:first-child .badge") });
        var nextRowLocator = rows.Nth(rowIndex).Locator("xpath=following-sibling::tr[1]");

        if (!await nextRowLocator.IsVisibleAsync())
            return null;

        var text = await nextRowLocator.Locator("td").TextContentAsync();
        if (text?.Contains("Cancellation Reason:") == true)
        {
            // Extract the text after "Cancellation Reason: "
            var reasonText = text.Replace("Cancellation Reason:", "").Trim();
            return string.IsNullOrEmpty(reasonText) ? null : reasonText;
        }

        return null;
    }
}
