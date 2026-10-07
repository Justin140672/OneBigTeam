using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public static class ReportExport
{
    public static async Task<IDownload> ExportAsync(IPage page, string formatLabel)
    {
        var downloadTask = page.WaitForDownloadAsync(new() { Timeout = 60_000 });
        var exportButton = page.GetByRole(AriaRole.Button, new() { Name = "Export" });
        var menuItem = page.GetByRole(AriaRole.Menuitem, new() { Name = formatLabel });

        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            await exportButton.ClickAsync();
            try
            {
                await menuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 4_000 });
                break;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
                await page.Keyboard.PressAsync("Escape");
                await page.WaitForTimeoutAsync(300);
            }
        }

        await menuItem.ClickAsync();

        var errorBanner = page.Locator(".alert-danger").First;
        var errorTask = errorBanner.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        var finished = await Task.WhenAny(downloadTask, errorTask);
        if (finished == downloadTask || errorTask.IsFaulted)
        {
            _ = errorTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return await downloadTask;
        }

        _ = downloadTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        var message = (await errorBanner.TextContentAsync())?.Trim();
        throw new InvalidOperationException($"Export did not produce a download; the page showed an error: '{message}'.");
    }
}
