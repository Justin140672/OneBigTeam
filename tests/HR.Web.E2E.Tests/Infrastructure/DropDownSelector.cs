using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// The single shared way to select a value from a Syncfusion SfDropDownList (rendered as
/// span[role='combobox'], opening a ".e-popup.e-ddl" popup of ".e-list-item" entries — with an
/// "input.e-input" filter textbox inside the popup only when AllowFiltering="true" on the
/// component). Click the combobox, wait for the popup, type into the filter input if one is
/// present, then click the matching item. No retry loop and no explicit wait for the popup to
/// close afterward — this is deliberately the simplest of the approaches page objects in this
/// project had converged on independently; it's the one that's proven reliable in practice.
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
        await scope.Locator("span[role='combobox']").Nth(index).ClickAsync();
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
    }
}
