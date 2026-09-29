using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class AdminQuickNavComponent(IPage page)
{
    public ILocator Trigger => page.GetByRole(AriaRole.Button, new() { Name = "Search employees" });

    public ILocator Dialog => page.GetByRole(AriaRole.Dialog);

    public ILocator Input => Dialog.GetByRole(AriaRole.Combobox);

    public ILocator IncludeLeaversCheckbox => Dialog.GetByRole(AriaRole.Checkbox);

    public ILocator Options => page.GetByRole(AriaRole.Option);

    public ILocator NoMatchesMessage => Dialog.GetByText("No matching employees");

    /// <summary>
    /// Presses Ctrl+K exactly once. Used by the "Ctrl+K is inert for a non-HR user" tests, which
    /// deliberately need a single press that is expected to do nothing. Tests that expect the
    /// palette to actually open should use <see cref="OpenAsync"/> instead.
    /// </summary>
    public async Task OpenWithKeyboardAsync()
    {
        await page.Keyboard.PressAsync("Control+k");
    }

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
            }
        }

        throw new TimeoutException(
            "Employee search palette did not open after repeated Ctrl+K presses, despite the trigger being visible.");
    }

    public async Task<bool> IsOpenAsync() =>
        await Dialog.IsVisibleAsync();

    public async Task WaitForOpenAsync()
    {
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task SearchAsync(string term)
    {
        await Input.FillAsync(term);
    }

    public async Task SetIncludeLeaversAsync(bool included)
    {
        await IncludeLeaversCheckbox.SetCheckedAsync(included);
    }

    public ILocator ResultsContaining(string text) => Options.Filter(new() { HasText = text });

    public async Task WaitForResultsSettledAsync(int timeoutMs = 10_000)
    {
        await Options.First.Or(NoMatchesMessage).WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
    }

    public async Task<bool> HasResultAsync(string text, int timeoutMs = 10_000)
    {
        return await ResultsContaining(text).First.WaitUntilVisibleAsync(timeoutMs);
    }

    public async Task AssertNoResultAsync(string text, int timeoutMs = 10_000)
    {
        await WaitForResultsSettledAsync(timeoutMs);
        await Assertions.Expect(ResultsContaining(text)).ToHaveCountAsync(0, new() { Timeout = timeoutMs });
    }

    public async Task ActivateFirstResultAsync()
    {
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");
    }

    public async Task ClickResultAsync(string text)
    {
        await ResultsContaining(text).First.ClickAsync();
    }

    public async Task PressEscapeAsync()
    {
        await page.Keyboard.PressAsync("Escape");
    }
}
