using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public static class LocatorExtensions
{
    public static async Task<bool> WaitUntilVisibleAsync(this ILocator locator, int timeoutMs = 10_000)
    {
        try
        {
            await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public static async Task RevealGridRowAsync(this IPage page, string text)
    {
        if (!await page.HasGridCellOnAnyPageAsync(text))
            throw new InvalidOperationException($"Grid row containing '{text}' was not found on any page.");
    }

    public static async Task<bool> HasGridCellOnAnyPageAsync(this IPage page, string text)
    {
        var match = page.Locator(".e-grid .e-rowcell").Filter(new() { HasText = text }).First;
        if (await match.WaitUntilVisibleAsync(5_000))
            return true;

        await ClickGridPagerAndWaitAsync(page, ".e-grid .e-pager .e-first:not(.e-disable)");

        for (var guard = 0; guard < 50; guard++)
        {
            if (await match.IsVisibleAsync())
                return true;

            if (!await ClickGridPagerAndWaitAsync(page, ".e-grid .e-pager .e-next:not(.e-disable)"))
                return false;
        }

        return false;
    }

    public static async Task ClickGridAddAndWaitForCreateRouteAsync(this IPage page, string createUrlGlob)
    {
        var addButton = page.GetByRole(AriaRole.Button, new() { Name = "Add" });
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        var listUrl = page.Url;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (page.Url != listUrl)
                return;

            try
            {
                await addButton.ClickAsync(new() { Timeout = 10_000 });
                await page.WaitForURLAsync(createUrlGlob, new() { Timeout = 10_000, WaitUntil = WaitUntilState.Commit });
                return;
            }
            catch (TimeoutException) when (attempt < 3)
            {
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"Clicking 'Add' did not navigate to '{createUrlGlob}' after 3 attempts (page is at {page.Url}).", ex);
            }
        }
    }

    public static async Task VisitAllGridPagesAsync(this IPage page, Func<Task<bool>> visitPage, TimeSpan budget)
    {
        await ClickGridPagerAndWaitAsync(page, ".e-grid .e-pager .e-first:not(.e-disable)");

        var deadline = DateTime.UtcNow + budget;
        while (true)
        {
            if (await visitPage())
                return;

            if (DateTime.UtcNow >= deadline)
                return;

            if (!await ClickGridPagerAndWaitAsync(page, ".e-grid .e-pager .e-next:not(.e-disable)"))
                return;
        }
    }

    private static async Task<bool> ClickGridPagerAndWaitAsync(IPage page, string pagerSelector)
    {
        var control = page.Locator(pagerSelector).First;
        if (await control.CountAsync() == 0)
            return false;

        var firstRow = page.Locator(".e-grid .e-row").First;
        var before = await firstRow.CountAsync() > 0 ? await firstRow.InnerTextAsync() : "";
        await control.ClickAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline &&
               (await firstRow.CountAsync() == 0 || await firstRow.InnerTextAsync() == before))
        {
            await page.WaitForTimeoutAsync(100);
        }

        return true;
    }

    public static async Task WaitForSpinnerToClearAsync(this IPage page, int appearTimeoutMs = 2_000, int clearTimeoutMs = 15_000)
    {
        try
        {
            await page.Locator(".spinner-border").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = appearTimeoutMs });
        }
        catch (TimeoutException)
        {
        }

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = clearTimeoutMs });
    }
}
