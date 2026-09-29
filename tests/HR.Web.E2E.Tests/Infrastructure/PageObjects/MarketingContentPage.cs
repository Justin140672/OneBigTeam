using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

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

    private ILocator FeaturePublishedCell(string title) =>
        FeatureRow(title).First.Locator("td").Nth(4);

    public Task<string?> GetFeaturePublishedTextAsync(string title) =>
        FeaturePublishedCell(title).TextContentAsync();

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


    public async Task ToggleFeaturePublicationAsync(string title)
    {
        var cell = FeaturePublishedCell(title);
        var before = ((await cell.TextContentAsync()) ?? "").Trim();
        var row = FeatureRow(title).First;
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
