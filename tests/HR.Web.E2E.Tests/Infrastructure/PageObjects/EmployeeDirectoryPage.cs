using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class EmployeeDirectoryPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/directory");
        await WaitForInteractiveAsync();
    }

    public async Task WaitForInteractiveAsync()
    {
        await page.WaitForSelectorAsync("[data-testid='directory-search']", new() { Timeout = 20_000 });
        await page.WaitForSelectorAsync(
            "[data-testid='directory-employee-card'], :text('No employees found')",
            new() { Timeout = 20_000 });
    }

    public ILocator Heading => page.GetByRole(AriaRole.Heading, new() { Name = "Employee Directory" });

    private ILocator SearchBox => page.Locator(
        "input[data-testid='directory-search'], [data-testid='directory-search'] input").First;

    private ILocator Cards => page.Locator("[data-testid='directory-employee-card']");

    public async Task SearchAsync(string term)
    {
        await SearchBox.FillAsync(term);
        await SearchBox.PressAsync("Tab");

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            if (await page.GetByText("No employees found").IsVisibleAsync())
                return;

            if (await Cards.CountAsync() > 0
                && await Cards.Filter(new() { HasNotText = term }).CountAsync() == 0)
                return;

            await page.WaitForTimeoutAsync(200);
        }
    }

    public Task<int> CardCount() => Cards.CountAsync();

    public async Task<bool> IsEmptyStateVisibleAsync() =>
        await page.GetByText("No employees found").IsVisibleAsync();

    public ILocator CardByName(string name) =>
        Cards.Filter(new() { HasText = name }).First;

    public async Task<ILocator> OpenCardAsync(string name)
    {
        var card = CardByName(name);
        await card.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await card.ClickAsync();

        var dialog = page.Locator("[data-testid='directory-detail']");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        return dialog;
    }
}
