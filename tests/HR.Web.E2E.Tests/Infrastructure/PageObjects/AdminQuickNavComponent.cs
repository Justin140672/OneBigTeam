using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// ADM-07 — the permission-aware quick-navigation palette (Ctrl+K). Locators are role/aria based
/// per the E2E locator conventions: the trigger button, the palette dialog and each result option.
/// </summary>
public sealed class AdminQuickNavComponent(IPage page)
{
    public ILocator Trigger => page.GetByRole(AriaRole.Button, new() { Name = "Quick navigation" });

    public ILocator Dialog => page.GetByRole(AriaRole.Dialog);

    public ILocator Options => page.GetByRole(AriaRole.Option);

    public async Task OpenWithKeyboardAsync()
    {
        await page.Keyboard.PressAsync("Control+k");
    }

    public async Task<bool> IsOpenAsync() =>
        await Dialog.IsVisibleAsync();

    public async Task WaitForOpenAsync()
    {
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task TypeAsync(string term)
    {
        await Dialog.GetByRole(AriaRole.Textbox).FillAsync(term);
    }

    public async Task<bool> HasResultAsync(string label)
    {
        var option = Options.Filter(new() { HasText = label }).First;
        return await option.WaitUntilVisibleAsync();
    }

    public async Task ActivateFirstResultAsync()
    {
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");
    }

    public async Task ClickResultAsync(string label)
    {
        await Options.Filter(new() { HasText = label }).First.ClickAsync();
    }

    public async Task PressEscapeAsync()
    {
        await page.Keyboard.PressAsync("Escape");
    }
}
