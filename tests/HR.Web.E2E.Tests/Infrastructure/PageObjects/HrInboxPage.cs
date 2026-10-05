using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class HrInboxPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, string? search = null)
    {
        var query = string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}";
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/hr/inbox{query}");
        await page.WaitForSelectorAsync(".inbox-card, .inbox-empty", new() { Timeout = 30_000 });
    }

    public async Task<bool> IsEmptyAsync() =>
        await page.Locator(".inbox-empty").IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetTaskTitlesAsync()
    {
        var titleEls = await page.Locator(".inbox-card-title").AllAsync();
        var titles = new List<string>();
        foreach (var t in titleEls)
            titles.Add((await t.TextContentAsync())?.Trim() ?? "");
        return titles;
    }

    public async Task ClaimAsync(string titleFragment)
    {
        var card = page.Locator(".inbox-card")
            .Filter(new() { HasText = titleFragment })
            .First;

        var cardHandle = await card.ElementHandleAsync();

        await card.GetByRole(AriaRole.Button, new() { Name = "Claim" }).ClickAsync();

        await cardHandle.WaitForElementStateAsync(ElementState.Hidden, new() { Timeout = 15_000 });
    }

    public async Task<string?> GetOwnerBadgeTextAsync(string titleFragment) =>
        (await page.Locator(".inbox-card")
            .Filter(new() { HasText = titleFragment })
            .First
            .Locator("[data-testid^='inbox-owner-']")
            .TextContentAsync())?.Trim();

    public async Task<bool> HasTaskAsync(string titleFragment) =>
        await page.Locator(".inbox-card")
            .Filter(new() { HasText = titleFragment })
            .IsVisibleAsync();
}
