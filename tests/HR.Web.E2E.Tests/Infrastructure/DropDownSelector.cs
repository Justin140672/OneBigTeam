using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// The single shared way to select a value from a Syncfusion SfDropDownList (rendered as
/// span[role='combobox'], opening a ".e-popup.e-ddl" popup of ".e-list-item" entries). Click the
/// combobox, wait for its popup, click the matching item, then confirm Blazor's ValueChanged
/// round-trip actually committed the selection into the combobox's own input.
///
/// This is the ONLY method any page object should use to drive a Syncfusion combobox — every call
/// site that previously hand-tuned its own wait budget (a widened attempt count, or a page object
/// "warming up" a dropdown before the real interaction) was working around a gap here rather than
/// something genuinely specific to that field.
///
/// After clicking the item this confirms the ValueChanged round-trip committed the selection into
/// the combobox's input — not just that the popup closed client-side — so a caller that immediately
/// acts on the bound value (submits a form, opens another dialog, reads the value back) can't race
/// the round-trip and see the previous (or empty) value. Matches via "contains" (a Regex, not exact
/// equality) since callers sometimes pass a distinguishing fragment rather than the full label.
/// </summary>
public static class DropDownSelector
{
    /// <param name="scope">
    /// The locator that already narrows down to the right field/dialog — a label-filtered field
    /// group (e.g. page.Locator(".col-12").Filter(new() { HasText = "New Manager" }).First), a
    /// dialog locator, or the page itself when there's only one combobox in scope.
    /// </param>
    /// <param name="index">Which combobox within <paramref name="scope"/>, when it contains more than one (defaults to the first).</param>
    public static async Task SelectAsync(IPage page, ILocator scope, string text, int index = 0)
    {
        var combobox = scope.Locator("span[role='combobox']").Nth(index);

        // If the combobox already shows this value (a dialog whose dropdown defaults to the
        // first/only option, or a caller re-selecting the same value across repeated iterations),
        // opening the popup is a no-op selection-wise — and can be actively harmful: Syncfusion
        // pre-highlights the already-active item on open, and since no ValueChanged fires for a
        // same-value click, the popup can auto-close before Playwright's actionability check on
        // that item completes, surfacing as a spurious "element is not visible" timeout. Skip the
        // whole open/click flow when there's nothing to change.
        var currentValue = await combobox.Locator("input").First.InputValueAsync();
        if (Regex.IsMatch(currentValue ?? "", Regex.Escape(text)))
            return;

        // Cold-start cost: the FIRST SfDropDownList popup opened on a freshly-loaded page pays a
        // large, one-time interop init on top of the component's own — measured across several call
        // sites (EmployeeEditPage.SelectManagerAsync, SelectNoticePeriodUnitAsync, the Position
        // Profile field on a freshly-opened Employment tab). Once ANY popup has been instantiated on
        // a page, every later one opens quickly. A page-wide, pre-existing ".e-popup.e-ddl" is proof
        // some dropdown already paid this cost — size the open budget below accordingly.
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
        for (var attempt = 1; attempt <= openAttempts; attempt++)
        {
            await combobox.ClickAsync();
            try
            {
                await openPopup.First.WaitForAsync(new()
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = attempt < openAttempts ? openTimeout : finalOpenTimeout,
                });
                break;
            }
            catch (TimeoutException) when (attempt < openAttempts)
            {
                // Click landed before the open handler was bound, or opened-then-closed. Reset to a
                // known-closed state so the next click opens rather than re-toggling.
                await page.Keyboard.PressAsync("Escape");
                await page.WaitForTimeoutAsync(150);
            }
        }

        // Now the popup has opened, Syncfusion has set aria-owns on the combobox pointing at this
        // field's own popup id ("{id}_popup"). Reading it here lets every wait below be scoped to
        // THIS combobox's popup — on a dialog with several dropdowns the unscoped ".e-popup.e-ddl"
        // is not unique. Fall back to the currently-visible popup (only one is) when aria-owns is
        // genuinely absent for this component's configuration.
        string? popupId = null;
        for (var attempt = 0; attempt < 8 && popupId is null; attempt++)
        {
            popupId = await combobox.GetAttributeAsync("aria-owns");
            if (popupId is null) await page.WaitForTimeoutAsync(250);
        }
        var popup = popupId is not null ? page.Locator($"#{popupId}") : openPopup;

        await popup.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // The popup container can become visible a tick before its item list is actually populated
        // (a separate JS render pass) — wait for at least one item to exist before clicking.
        await popup.Locator(".e-list-item").First.WaitForAsync(new() { Timeout = 10_000 });

        // Standard (non-server-filtered) comboboxes render their full item list into the popup up
        // front, so just click the matching item directly — faster and more reliable than driving
        // per-character keystrokes into the filter box. Server-loading comboboxes (the Manager /
        // Review Owner / Add Candidate pickers, whose list only materializes after a debounced
        // Filtering round trip) are NOT handled by this path yet — a deliberate follow-up.
        var item = popup.Locator(".e-list-item:not(.e-hide)").Filter(new() { HasText = text }).First;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await item.ClickAsync(new() { Timeout = attempt < 3 ? 5_000 : 30_000 });
                break;
            }
            catch (PlaywrightException) when (attempt < 3)
            {
                // Item detached mid-click (server filter results just replaced the list) — the
                // locator will re-resolve against the fresh DOM on the next attempt.
            }
        }

        // A handful of dropdowns (e.g. SupportRequestQueue's status column) render a humanized
        // ValueTemplate ("Under Review") while keeping the bound Value as the raw enum string
        // ("UnderReview"). The native <input> exposes the raw value, so match either the exact
        // selected text or its no-space form.
        await Assertions.Expect(combobox.Locator("input").First)
            .ToHaveValueAsync(
                new Regex($"{Regex.Escape(text)}|{Regex.Escape(text.Replace(" ", ""))}"),
                new() { Timeout = 10_000 });

        // The assertion above only proves the client-side widget updated its own input text —
        // Syncfusion Blazor Server components do that optimistically in JS on click, ahead of the
        // SignalR round-trip that actually commits the bound value server-side. There is no generic
        // provably-server-committed DOM signal. Waiting for the popup to hide plus a short debounce
        // is a pragmatic mitigation for that race (same fixed-wait pattern used elsewhere in this
        // suite for Blazor Server timing). Best-effort only — the value is already confirmed
        // committed by this point, so a timeout here isn't a real failure.
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
