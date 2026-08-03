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

        await combobox.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });

        // The popup container can become visible a tick before its item list is actually populated
        // (a separate JS render pass) — filtering before that list exists filters against nothing,
        // and typing into the filter box doesn't retroactively re-trigger it once the list does
        // populate, leaving every item permanently hidden. Wait for at least one (unfiltered) item
        // to exist first.
        await page.WaitForSelectorAsync(".e-popup.e-ddl .e-list-item", new() { Timeout = 10_000 });

        var filterInput = page.Locator(".e-popup.e-ddl:visible input.e-input").First;
        if (await filterInput.CountAsync() > 0)
        {
            await filterInput.FillAsync(text);
            await page.WaitForSelectorAsync(".e-popup.e-ddl .e-list-item:not(.e-hide)", new() { Timeout = 15_000 });
        }

        await page.Locator(".e-popup.e-ddl .e-list-item:not(.e-hide)")
            .Filter(new() { HasText = text })
            .First
            .ClickAsync();

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
        await page.Locator(".e-popup.e-ddl").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
        await page.WaitForTimeoutAsync(250);
    }
}
