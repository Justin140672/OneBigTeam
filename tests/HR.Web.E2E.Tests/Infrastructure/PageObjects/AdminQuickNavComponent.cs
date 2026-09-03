using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// The HR-only Employee Search palette (Ctrl+K), rendered in the top bar only for HR
/// administrators (see AdminQuickNav.razor's <c>@if (Session.IsHrAdministrator)</c>). Locators are
/// role/aria based per the E2E locator conventions: the trigger button, the palette dialog and
/// each result option (an employee row).
/// </summary>
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
            "Employee search palette did not open after repeated Ctrl+K presses, despite the trigger being visible.");
    }

    public async Task<bool> IsOpenAsync() =>
        await Dialog.IsVisibleAsync();

    public async Task WaitForOpenAsync()
    {
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    /// <summary>Types (replacing) the search term. The component debounces the query ~250 ms.</summary>
    public async Task SearchAsync(string term)
    {
        await Input.FillAsync(term);
    }

    /// <summary>Ticks / unticks the "Include leavers / archived employees" checkbox, re-running the query.</summary>
    public async Task SetIncludeLeaversAsync(bool included)
    {
        await IncludeLeaversCheckbox.SetCheckedAsync(included);
    }

    /// <summary>All result rows whose visible text contains <paramref name="text"/> (name, employee number, position…).</summary>
    public ILocator ResultsContaining(string text) => Options.Filter(new() { HasText = text });

    /// <summary>
    /// Waits for the debounced query to settle — either at least one result row or the explicit
    /// "No matching employees" empty state is shown.
    /// </summary>
    public async Task WaitForResultsSettledAsync(int timeoutMs = 10_000)
    {
        await Options.First.Or(NoMatchesMessage).WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
    }

    public async Task<bool> HasResultAsync(string text, int timeoutMs = 10_000)
    {
        return await ResultsContaining(text).First.WaitUntilVisibleAsync(timeoutMs);
    }

    /// <summary>Asserts that, once the query has settled, no result row matches <paramref name="text"/>.</summary>
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
