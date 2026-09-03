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

    /// <summary>
    /// Presses Ctrl+K exactly once. Used by the "Ctrl+K is inert for a plain employee" test, which
    /// deliberately needs a single press that is expected to do nothing. Tests that expect the
    /// palette to actually open should use <see cref="OpenAsync"/> instead.
    /// </summary>
    public async Task OpenWithKeyboardAsync()
    {
        await page.Keyboard.PressAsync("Control+k");
    }

    /// <summary>
    /// Opens the palette robustly. app.js's global Ctrl+K keydown listener only forwards to the
    /// component once its first interactive render has registered a DotNetObjectReference
    /// (registerAdminQuickNav) — a press that lands before that is silently dropped with no retry.
    /// So wait for the trigger button to render (the same OnAfterRenderAsync(firstRender) that
    /// registers the handler) and re-press Ctrl+K until the dialog appears.
    /// </summary>
    public async Task OpenAsync()
    {
        await Trigger.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        for (var attempt = 0; attempt < 15; attempt++)
        {
            await page.Keyboard.PressAsync("Control+k");
            try
            {
                await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 1_000 });
                return;
            }
            catch (TimeoutException)
            {
                // The DotNet handler isn't live yet — press again on the next tick.
            }
        }

        throw new TimeoutException(
            "Quick-nav palette did not open after repeated Ctrl+K presses, despite the trigger being visible.");
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
