using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's MarketingContent.razor (/marketing-content) — the platform-admin
/// page for managing the public marketing site's product features and roadmap items via
/// MarketingContentAdminService (endpoints /api/marketing/admin/*, policy "platform:admin").
///
/// The page renders two "admin-table" tables (Features first, Roadmap second), each row carrying
/// ↑/↓ reorder SfButtons plus "Edit" and a "Publish"/"Unpublish" toggle SfButton. "+ Add feature" /
/// "+ Add roadmap item" open an inline edit section (SfTextBox fields; a plain HTML
/// &lt;select class="admin-select"&gt; for delivery status, so Playwright's SelectOptionAsync is
/// used rather than the DropDownSelector helper) with a primary "Save feature" / "Save roadmap item"
/// button. On success MarketingContent.razor shows &lt;p class="admin-action-success"&gt;Saved.&lt;/p&gt;
/// and reloads the tables in-place; errors render &lt;ul class="admin-action-error"&gt;.
///
/// SfTextBox's server-side bound value only round-trips over the Blazor Server circuit on
/// blur/change, not on FillAsync's raw "input" DOM event alone (same convention documented on
/// AdminLoginPage.LoginAsync / SettingsPage), so every fill here is followed by a Tab.
/// </summary>
public sealed class MarketingContentPage(IPage page, string baseUrl)
{
    private const string SettledSelector = ".dashboard-error, table.admin-table";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/marketing-content");
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 20_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    // Features table is the first .admin-table, Roadmap the second.
    private ILocator FeaturesTable => page.Locator("table.admin-table").Nth(0);

    private ILocator RoadmapTable => page.Locator("table.admin-table").Nth(1);

    public Task<bool> IsFeaturesTableVisibleAsync() => FeaturesTable.IsVisibleAsync();

    private ILocator FeatureRow(string title) =>
        FeaturesTable.Locator("tbody tr").Filter(new() { HasText = title });

    private ILocator RoadmapRow(string title) =>
        RoadmapTable.Locator("tbody tr").Filter(new() { HasText = title });

    public async Task<bool> HasFeatureAsync(string title)
    {
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        return await FeatureRow(title).First.IsVisibleAsync();
    }

    public async Task<bool> HasRoadmapItemAsync(string title)
    {
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        return await RoadmapRow(title).First.IsVisibleAsync();
    }

    // Features columns: Order, Title, Slug, Delivery, Published, actions.
    private ILocator FeaturePublishedCell(string title) =>
        FeatureRow(title).First.Locator("td").Nth(4);

    public Task<string?> GetFeaturePublishedTextAsync(string title) =>
        FeaturePublishedCell(title).TextContentAsync();

    // Roadmap columns: Order, Title, Delivery, Published, actions.
    private ILocator RoadmapPublishedCell(string title) =>
        RoadmapRow(title).First.Locator("td").Nth(3);

    public Task<string?> GetRoadmapPublishedTextAsync(string title) =>
        RoadmapPublishedCell(title).TextContentAsync();

    public async Task<List<string>> GetFeatureTitlesInOrderAsync()
    {
        var cells = FeaturesTable.Locator("tbody tr td:nth-child(2)");
        var count = await cells.CountAsync();
        var titles = new List<string>(count);
        for (var i = 0; i < count; i++)
            titles.Add(((await cells.Nth(i).TextContentAsync()) ?? "").Trim());
        return titles;
    }

    // --- Inline edit form ---

    private ILocator EditField(string label) =>
        page.Locator(".admin-action-field").Filter(new() { Has = page.Locator("label", new() { HasText = label }) });

    private async Task FillFieldAsync(string label, string value)
    {
        var input = EditField(label).Locator("input, textarea").First;
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task ClickAddFeatureAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "+ Add feature" }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "New feature" }).WaitForAsync(new() { Timeout = 10_000 });
    }

    public async Task ClickAddRoadmapItemAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "+ Add roadmap item" }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "New roadmap item" }).WaitForAsync(new() { Timeout = 10_000 });
    }

    public async Task ClickEditFeatureAsync(string title)
    {
        await FeatureRow(title).First.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Edit feature" }).WaitForAsync(new() { Timeout = 10_000 });
    }

    /// <summary>Fills the feature edit form. Only pass the fields you want to set.</summary>
    public async Task FillFeatureFormAsync(
        string? slug = null,
        string? title = null,
        string? iconName = null,
        string? summary = null,
        string? deliveryStatus = null)
    {
        if (slug is not null) await FillFieldAsync("Slug", slug);
        if (title is not null) await FillFieldAsync("Title", title);
        if (iconName is not null) await FillFieldAsync("Icon name", iconName);
        if (summary is not null) await FillFieldAsync("Summary", summary);
        if (deliveryStatus is not null)
            await page.Locator("select.admin-select").First.SelectOptionAsync(new SelectOptionValue { Value = deliveryStatus });
    }

    public async Task FillRoadmapFormAsync(
        string? title = null,
        string? description = null,
        string? iconName = null,
        string? deliveryStatus = null)
    {
        if (title is not null) await FillFieldAsync("Title", title);
        if (description is not null) await FillFieldAsync("Description", description);
        if (iconName is not null) await FillFieldAsync("Icon name", iconName);
        if (deliveryStatus is not null)
            await page.Locator("select.admin-select").First.SelectOptionAsync(new SelectOptionValue { Value = deliveryStatus });
    }

    // On a successful save MarketingContent.razor closes the inline edit section (sets
    // _editingFeature/_editingRoadmap = null); on failure the section stays open and renders
    // <ul class="admin-action-error">. Waiting on the shared success banner is unreliable here
    // because a prior action's "Saved." banner is still in the DOM (SaveFeature/SaveRoadmap don't
    // reset it), so a stale banner would satisfy the wait immediately.
    public async Task SaveFeatureAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save feature", Exact = true }).ClickAsync();
        await WaitForSaveSettledAsync("Save feature");
    }

    public async Task SaveRoadmapItemAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save roadmap item", Exact = true }).ClickAsync();
        await WaitForSaveSettledAsync("Save roadmap item");
    }

    private async Task WaitForSaveSettledAsync(string saveButtonName)
    {
        var saveButton = page.GetByRole(AriaRole.Button, new() { Name = saveButtonName, Exact = true });
        var errorList = page.Locator("ul.admin-action-error");
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (await errorList.IsVisibleAsync())
                return;
            if (await saveButton.CountAsync() == 0)
                return;
            await page.WaitForTimeoutAsync(100);
        }
    }

    // --- Row toggles / reorder ---

    // The Published cell flip is the authoritative signal that the mutation round-tripped and the
    // table reloaded in-place. The shared "Saved."/error banner can't be used to wait here: the
    // razor page's ToggleFeaturePublication resets the banner and re-adds it after the round-trip,
    // but a stale "Saved." <p> from the previous step stays in the DOM long enough to satisfy a
    // plain WaitForAsync before the click even registers.
    public async Task ToggleFeaturePublicationAsync(string title)
    {
        var cell = FeaturePublishedCell(title);
        var before = ((await cell.TextContentAsync()) ?? "").Trim();
        var row = FeatureRow(title).First;
        // "Unpublish" also matches a substring "Publish" search, so resolve by exact accessible name.
        var unpublish = row.GetByRole(AriaRole.Button, new() { Name = "Unpublish", Exact = true });
        if (await unpublish.CountAsync() > 0)
            await unpublish.ClickAsync();
        else
            await row.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Assertions.Expect(cell).ToHaveTextAsync(before == "Yes" ? "No" : "Yes", new() { Timeout = 15_000 });
    }

    public async Task ToggleRoadmapPublicationAsync(string title)
    {
        var cell = RoadmapPublishedCell(title);
        var before = ((await cell.TextContentAsync()) ?? "").Trim();
        var row = RoadmapRow(title).First;
        var unpublish = row.GetByRole(AriaRole.Button, new() { Name = "Unpublish", Exact = true });
        if (await unpublish.CountAsync() > 0)
            await unpublish.ClickAsync();
        else
            await row.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Assertions.Expect(cell).ToHaveTextAsync(before == "Yes" ? "No" : "Yes", new() { Timeout = 15_000 });
    }

    public async Task MoveFeatureUpAsync(string title)
    {
        var before = await GetFeatureTitlesInOrderAsync();
        var startIndex = before.FindIndex(t => t == title);
        await FeatureRow(title).First.GetByRole(AriaRole.Button, new() { Name = "↑" }).ClickAsync();
        // Wait for the in-place table reload to actually move the row up one position.
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var now = await GetFeatureTitlesInOrderAsync();
            if (now.FindIndex(t => t == title) == startIndex - 1)
                return;
            await page.WaitForTimeoutAsync(100);
        }
    }

    public Task<bool> IsSuccessBannerVisibleAsync() =>
        page.Locator("p.admin-action-success").IsVisibleAsync();

    public Task<string?> GetErrorListTextAsync() =>
        page.Locator("ul.admin-action-error").TextContentAsync();
}
