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
/// This is the ONLY method any page object should use to drive a Syncfusion combobox — every
/// call site that previously hand-tuned its own wait budget (e.g. a widened "ariaOwnsAttempts"
/// override, or a page object pre-"warming up" a dropdown before the real interaction) was
/// working around a gap in this method rather than something genuinely specific to that field.
/// The cold-start detection below closes that gap here, once, for every caller — see its own
/// remarks for why per-call-site tuning kept recurring and wasn't the fix.
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

        // The FIRST SfDropDownList popup ever opened on a freshly-loaded page pays a large,
        // one-time cold-start cost on top of any individual component's own interop init — this
        // was independently rediscovered (and worked around ad hoc) at several different call
        // sites: EmployeeEditPage.SelectManagerAsync, SelectNoticePeriodUnitAsync, the Position
        // Profile field on a freshly-opened Employment tab, and OpenEmploymentTabAsync growing its
        // own "click the Manager combobox open and Escape it" warm-up step. Comparing a reliably
        // fast case (PositionProfileEditPage's own notice-period Unit dropdown, reached only after
        // its test already opened several OTHER dropdowns earlier on the same page) against a
        // reliably slow one (the same field on the Employment tab, where it was the first dropdown
        // ever touched on that page) confirmed it: once ANY popup has ever been instantiated on a
        // page, every later one opens quickly; the very first one can take dramatically longer.
        // Detect that directly — a page-wide, pre-existing ".e-popup.e-ddl" is proof some dropdown
        // already paid this cost — and size every budget below accordingly, automatically, instead
        // of requiring each caller to know and tune for it.
        // The cold-case ceilings below are a MAX wait, not a fixed sleep — every poll breaks as
        // soon as its condition is met, so a page that's merely a little slow isn't taxed the
        // full budget. They only get fully paid by a test that's genuinely stuck, and only then
        // do they matter for wall-clock. Original cold numbers (60s/45s) were measured during an
        // already-contended session (this same debugging session had been hammering the shared
        // dev app with concurrent builds/reruns all day); dialed back here since every test gets
        // a fresh page (so EVERY test's first dropdown is "cold" by this check) and 20-way
        // concurrency multiplies any per-test tail cost across the whole suite's wall-clock —
        // still well above the original 5s/10s that caused real failures, just not as extreme.
        var pageAlreadyWarm = await page.Locator(".e-popup.e-ddl").CountAsync() > 0;
        var ariaOwnsAttempts = pageAlreadyWarm ? 20 : 80; // 5s warm / 20s cold, at 250ms/attempt

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
        // especially for a combobox that only mounts once an async data load completes and the
        // page/dialog re-renders. Reading it immediately (no wait) races that init, so poll for
        // the attribute to actually appear instead of trusting a single immediate read. For some
        // combobox configurations it never appears at all (not delayed — genuinely absent), in
        // which case this poll intentionally spends its full budget before falling back to the
        // unscoped ".e-popup.e-ddl" selector below.
        string? popupId = null;
        for (var attempt = 0; attempt < ariaOwnsAttempts && popupId is null; attempt++)
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
        //
        // When popupId is null (the aria-owns poll above never found it), the unscoped
        // ".e-popup.e-ddl" fallback used here still resolves instantly if pageAlreadyWarm — some
        // other dropdown's popup is already attached. When the page is cold too, give this the
        // same generous, cold-start-sized budget as the aria-owns poll above rather than the
        // page's ordinary fast-path timeouts.
        // 20s (a mid-session dial-back from an original 45s, for overall suite speed) turned out
        // consistently too tight specifically for Manager on a freshly-created employee's
        // Employment tab — last combobox in DOM order, behind that tab's own heaviest LoadAsync,
        // AND the first popup ever opened on that page (see this method's own remarks on why the
        // first-ever popup pays a much larger cold-start cost). Split the difference: keep the
        // cheaper per-attempt cost low, but give the cold case one extra attempt with a longer
        // final ceiling, rather than inflating every attempt's budget suite-wide again.
        var clickAttemptTimeout = pageAlreadyWarm ? 3_000 : 6_000;
        var finalClickAttemptTimeout = pageAlreadyWarm ? 10_000 : 30_000;
        var clickAttempts = pageAlreadyWarm ? 4 : 5;
        for (var attempt = 1; attempt <= clickAttempts; attempt++)
        {
            await combobox.ClickAsync();
            try
            {
                await popup.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = attempt < clickAttempts ? clickAttemptTimeout : finalClickAttemptTimeout });
                break;
            }
            catch (TimeoutException) when (attempt < clickAttempts)
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

        // A handful of dropdowns (e.g. SupportRequestQueue's status column) intentionally render a
        // humanized ValueTemplate ("Under Review") while keeping the bound Value as the raw enum
        // string ("UnderReview", no space) for the API payload — see EnumDisplay.Humanize's usage
        // there. The native <input> this locator reads exposes that raw value, not the template's
        // rendered text, so match either the exact selected text or its no-space form rather than
        // assuming the two always coincide (true for most dropdowns, false for those).
        await Assertions.Expect(combobox.Locator("input").First)
            .ToHaveValueAsync(
                new Regex($"{Regex.Escape(text)}|{Regex.Escape(text.Replace(" ", ""))}"),
                new() { Timeout = 10_000 });

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
        // Best-effort only, per the comment above — the value is already confirmed committed by
        // this point, so a timeout here (the popup taking longer than 5s to visually close under a
        // busy run) isn't a real failure and shouldn't fail the caller.
        try
        {
            await popup.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
        }

        await page.WaitForTimeoutAsync(250);
    }
}
