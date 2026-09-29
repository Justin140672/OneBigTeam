using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class CancelLeavingProcessDialog(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel Leaving Process" });

    public async Task OpenAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel Leaving Process", Exact = true }).ClickAsync();
        await Dialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    public async Task<bool> HasOffboardingTasksWarningAsync() =>
        await Dialog.Locator(".alert-warning").IsVisibleAsync();

    public async Task FillCancellationReasonAsync(string reason)
    {
        await Dialog.Locator("textarea").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task ConfirmAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel Leaving Process" }).ClickAsync();

        try
        {
            await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            await Dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
    }

    public async Task CloseAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task<string?> GetErrorAsync()
    {
        var error = Dialog.Locator(".alert-danger").First;
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }
}
