using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// The single shared way to select a value from a Syncfusion SfDropDownList (rendered as
/// span[role='combobox'], opening a ".e-popup.e-ddl" popup of ".e-list-item" entries — with an
/// "input.e-input" filter textbox inside the popup only when AllowFiltering="true" on the
/// component). Click the combobox, wait for the popup, type into the filter input if one is
/// present, then click the matching item.
///
/// After clicking the item, this also confirms Blazor's ValueChanged round-trip actually
/// committed the selection into the combobox's own input — not just that the popup closed
/// client-side. Without this, a caller that immediately acts on the bound value (submits a form,
/// opens another dialog, reads the value back) can race the round-trip and see the previous
/// (or empty) value. This used to be bolted on ad hoc at individual call sites
/// (EmployeeEditPage.SelectManagerAsync being the original); it's centralized here now so every
/// combobox selection in the suite gets it, not just the ones someone remembered to guard.
/// Matches via "contains" (a Regex, not exact equality) since callers sometimes pass a
/// distinguishing fragment rather than the item's full label.
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

        // Every popup-related wait below used to search the whole page for ".e-popup.e-ddl",
        // which is not unique: Syncfusion mounts one such element per SfDropDownList instance and
        // toggles it open/closed via a CSS class (e-popup-close) rather than adding/removing it
        // from the DOM (confirmed in Syncfusion.Blazor.DropDowns' own sf-dropdownlist.min.js —
        // "aria-owns":this.element.id+"_popup"). On a page with more than one dropdown (e.g. the
        // Upload/Edit Document dialogs' Category + Review Frequency + Review Owner), that made
        // these waits liable to resolve against a completely different dropdown's already-attached
        // (but closed) popup — passing every wait without this combobox's popup ever having
        // opened, then typing/clicking against nothing meaningful (observed: text silently landing
        // in whatever field still had focus instead of the intended popup). Resolve this
        // combobox's own popup id via aria-owns and scope every popup lookup to it specifically.
        //
        // aria-owns is added by Syncfusion's JS interop once it finishes initializing the
        // component — it is NOT necessarily present in the initial server-rendered HTML,
        // especially for a combobox that (like Category here) only mounts once an async data
        // load completes and the dialog re-renders. Reading it immediately (no wait) raced that
        // init: fast enough runs got null and silently fell back to the old ambiguous page-wide
        // ".e-popup.e-ddl" selector, reintroducing the exact bug this was meant to fix — just
        // intermittently instead of always (confirmed: stepping through slowly in Playwright
        // Inspector, which gives interop time to finish, made it work every time). Poll for the
        // attribute to actually appear instead of trusting a single immediate read.
        string? popupId = null;
        for (var attempt = 0; attempt < 20 && popupId is null; attempt++)
        {
            popupId = await combobox.GetAttributeAsync("aria-owns");
            if (popupId is null) await page.WaitForTimeoutAsync(250);
        }
        var popup = popupId is not null ? page.Locator($"#{popupId}") : page.Locator(".e-popup.e-ddl");

        // If the combobox already shows this value (e.g. a dialog whose dropdown defaults to the
        // first/only option, or a caller re-selecting the same value across repeated iterations),
        // opening the popup is a no-op selection-wise — and can be actively harmful: Syncfusion
        // pre-highlights the already-active item on open, and since no ValueChanged fires for a
        // same-value click, the popup can auto-close again before Playwright's actionability check
        // on that item completes, surfacing as a spurious "element is not visible" timeout. Skip
        // the whole open/click flow when there's nothing to change.
        var currentValue = await combobox.Locator("input").First.InputValueAsync();
        if (Regex.IsMatch(currentValue ?? "", Regex.Escape(text)))
            return;

        // A combobox that has only just mounted (e.g. the first field rendered right as a dialog's
        // _loading flips off) can have its DOM element visible before Syncfusion's JS interop has
        // actually attached the click listener that opens the popup — a real race, not a flake in
        // the test itself. A single click in that gap is silently swallowed with no popup and no
        // error, so retry the click when NOTHING happened at all. Retrying blindly on any timeout
        // is dangerous though: a popup backed by a large item list (e.g. a 500-employee picker) can
        // start opening but take a while to become visible — clicking again in that window toggles
        // it back closed and corrupts the selection that follows. So only treat it as "never
        // opened" (and click again) when the popup element hasn't even attached to the DOM yet;
        // once it's attached, keep waiting for it to become visible instead of re-clicking.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await combobox.ClickAsync();
            try
            {
                await popup.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = attempt < 3 ? 2_000 : 10_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 3)
            {
                // Popup never even attached — listener likely wasn't bound yet. Try again.
            }
        }

        await popup.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // The popup container can become visible a tick before its item list is actually populated
        // (a separate JS render pass) — filtering before that list exists filters against nothing,
        // and typing into the filter box doesn't retroactively re-trigger it once the list does
        // populate, leaving every item permanently hidden. Wait for at least one (unfiltered) item
        // to exist first.
        await popup.Locator(".e-list-item").First.WaitForAsync(new() { Timeout = 10_000 });

        var filterInput = popup.Locator("input.e-input").First;
        if (await filterInput.CountAsync() > 0)
        {
            // FillAsync sets the DOM value directly and fires one synthetic "input" event —
            // confirmed (via direct DB inspection ruling out missing/inactive data, and via a
            // Chromium background-timer-throttling fix that didn't help) not to reliably trigger
            // Syncfusion's own filter binder for this component, leaving the item list
            // permanently unfiltered/stuck and the ":not(.e-hide)" wait below timing out
            // deterministically rather than flakily. Real per-character keystrokes do trigger it.
            // Safe to always do this rather than only for client-side lists: a server-filtered
            // dropdown's Filtering handler re-fires per keystroke either way (see the request-id
            // guard in e.g. OnReviewOwnerFilteringAsync), so this doesn't add new behavior there,
            // just the same real-event path already required here.
            await filterInput.PressSequentiallyAsync(text, new() { Delay = 30 });

            // For a client-side AllowFiltering list, typing toggles ".e-hide" on non-matching items
            // synchronously, so waiting for "not(.e-hide)" is a real signal. For a server-filtered
            // dropdown (Filtering event handler, e.g. the Add Candidate picker's
            // OnCandidateFilteringAsync) nothing ever gets ".e-hide" — the whole item list is
            // replaced wholesale once a debounce + SignalR round trip + backend call complete, so
            // that same wait resolves instantly against the stale pre-search list instead of
            // actually waiting for anything. Give the round trip a head start before evaluating the
            // list at all — this is what actually shrinks the race window the retry loop below is
            // there to catch, not a substitute for it (a slow/loaded run can still take longer than
            // this).
            await page.WaitForTimeoutAsync(500);
            // 15s was fine for a solo run but under a full parallel headless run the shared
            // Aspire-hosted app gets busy enough that a server-filtered round trip (debounce +
            // SignalR + backend query) can legitimately take longer than that — same root cause
            // E2ETestBase's page-level default timeout was raised from 15s to 30s for. This wait
            // was missed when that fix went in.
            await popup.Locator(".e-list-item:not(.e-hide)").First.WaitForAsync(new() { Timeout = 30_000 });
        }

        // Some AllowFiltering dropdowns (e.g. the Add Candidate picker's OnCandidateFilteringAsync,
        // the Manager/Review Owner pickers) search server-side: typing kicks off a debounced HTTP
        // round trip that ends in a Blazor re-render replacing the ENTIRE popup item list, not the
        // client-side ".e-hide" class-toggle the wait above assumes. The item this locator resolves
        // to immediately after typing can be from the pre-round-trip render and get detached out
        // from under the click the moment the server response lands — a real race, not a flake in
        // the test itself. Retry the click itself (re-resolving the locator fresh each attempt,
        // which naturally picks up the post-round-trip DOM) rather than only retrying the popup-open
        // step above, since that's where a server-filtered list can still be mid-flight.
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

        await Assertions.Expect(combobox.Locator("input").First)
            .ToHaveValueAsync(new Regex(Regex.Escape(text)), new() { Timeout = 10_000 });

        // The assertion above only proves the client-side widget updated its own input text —
        // Syncfusion Blazor Server components do that optimistically, in JS, on click, ahead of
        // (and independent of) the SignalR round-trip that actually invokes the .NET
        // ValueChange/ValueChanged handler and commits the bound value server-side. There is no
        // generic, provably-server-committed DOM signal exposed by the component for this (no
        // aria-busy toggle, no spinner tied to value-commit specifically — those only exist ad hoc
        // on a handful of forms for unrelated async work). Waiting for the popup to close
        // (Syncfusion toggles it to hidden via the e-popup-close class rather than removing it
        // from the DOM, so waiting for Detached here is wrong and times out) plus a short fixed
        // debounce is a pragmatic, non-deterministic mitigation for that race, matching the
        // fixed-wait pattern already used elsewhere in this suite for the same class of Blazor
        // Server timing issue (see e.g. EmployeeListPage, CompanyEditPage). It reduces — it does
        // not guarantee — the race window; callers with a downstream element/condition that
        // reliably only appears post-commit should still wait on that directly rather than relying
        // solely on this.
        await popup.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
        await page.WaitForTimeoutAsync(250);
    }
}
