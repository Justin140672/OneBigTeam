using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public static class DropDownSelector
{
    public static async Task SelectAsync(IPage page, ILocator scope, string text, int index = 0)
    {
        var combobox = scope.Locator("span[role='combobox']").Nth(index);

        var currentValue = await combobox.Locator("input").First.InputValueAsync();
        if (Regex.IsMatch(currentValue ?? "", Regex.Escape(text)))
            return;

        var pageAlreadyWarm = await page.Locator(".e-popup.e-ddl").CountAsync() > 0;

        // Open THIS combobox's popup. A combobox that has only just mounted (e.g. the first field
        // rendered as a dialog's _loading flips off) can have its DOM element visible before
        // Syncfusion's JS interop has attached the click listener that opens the popup — a real
        // race. A click in that gap is silently swallowed with no popup and no error. Only ONE
        // ".e-popup.e-ddl" is visible at a time (Syncfusion mounts one per instance and toggles it
        // via a CSS class rather than DOM add/remove, closing any other when one opens), so a
        // visible popup is a reliable "my click landed" signal. On a retry, press Escape first to
        // return to a known-closed state — otherwise a click that opened a popup a moment too late
        // to be seen would just get toggled back closed by the next click.
        //
        // This deliberately does NOT poll for the combobox's aria-owns attribute *before* opening:
        // Syncfusion only sets aria-owns once the popup has opened at least once, so a pre-open poll
        // never resolves for many combobox configs and just burns its whole budget (5-20s per
        // field) before falling back anyway. aria-owns is read once, cheaply, AFTER the open below.
        var openPopup = page.Locator(".e-popup.e-ddl:visible");
        var openTimeout = pageAlreadyWarm ? 6_000 : 10_000;
        var finalOpenTimeout = pageAlreadyWarm ? 15_000 : 30_000;
        var openAttempts = pageAlreadyWarm ? 4 : 5;

        await combobox.HoverAsync(new() { Timeout = openTimeout });
        await page.WaitForTimeoutAsync(pageAlreadyWarm ? 150 : 350);

        for (var attempt = 1; attempt <= openAttempts; attempt++)
        {
            try
            {
                await combobox.ClickAsync(new() { Timeout = attempt < openAttempts ? openTimeout : finalOpenTimeout });
                await openPopup.First.WaitForAsync(new()
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = attempt < openAttempts ? openTimeout : finalOpenTimeout,
                });
                break;
            }
            catch (PlaywrightException) when (attempt < openAttempts)
            {
                await page.Keyboard.PressAsync("Escape");
                await page.WaitForTimeoutAsync(300);
            }
        }

        string? popupId = null;
        for (var attempt = 0; attempt < 8 && popupId is null; attempt++)
        {
            popupId = await combobox.GetAttributeAsync("aria-owns");
            if (popupId is null) await page.WaitForTimeoutAsync(250);
        }
        var popup = popupId is not null ? page.Locator($"#{popupId}") : openPopup;

        for (var reopen = 1; reopen <= 3; reopen++)
        {
            if (await popup.First.IsVisibleAsync()) break;

            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(300);
            await combobox.ClickAsync(new() { Timeout = finalOpenTimeout });
            try
            {
                await openPopup.First.WaitForAsync(new()
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = finalOpenTimeout,
                });
            }
            catch (PlaywrightException) when (reopen < 3) { continue; }

            popupId ??= await combobox.GetAttributeAsync("aria-owns");
            popup = popupId is not null ? page.Locator($"#{popupId}") : openPopup;
        }

        await popup.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = finalOpenTimeout });

        await popup.Locator(".e-list-item:not(.e-hide)").First.WaitForAsync(new() { Timeout = finalOpenTimeout });

        var item = popup.Locator(".e-list-item:not(.e-hide)").Filter(new() { HasText = text }).First;
        var foundInInitialList = true;
        try
        {
            await item.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        }
        catch (TimeoutException)
        {
            foundInInitialList = false;
        }

        if (!foundInInitialList)
        {
            var popupFilterInput = popup.Locator("span.e-filter-parent input.e-input").First;
            var filterInput = await popupFilterInput.CountAsync() > 0 ? popupFilterInput : combobox.Locator("input").First;

            await filterInput.ClickAsync();
            await filterInput.PressSequentiallyAsync(text, new() { Delay = 40 });

            item = popup.Locator(".e-list-item:not(.e-hide)").Filter(new() { HasText = text }).First;
            await item.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await item.ClickAsync(new()
                {
                    Timeout = attempt < 3 ? 5_000 : 30_000,
                    Force = attempt == 3,
                });
                break;
            }
            catch (PlaywrightException) when (attempt < 3)
            {
            }
        }

        await Assertions.Expect(combobox.Locator("input").First)
            .ToHaveValueAsync(
                new Regex($"{Regex.Escape(text)}|{Regex.Escape(text.Replace(" ", ""))}"),
                new() { Timeout = 10_000 });

        try
        {
            await popup.First.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
        }

        await page.WaitForTimeoutAsync(250);
    }
}
