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
        await combobox.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });

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
    }
}
