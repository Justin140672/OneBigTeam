using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class AmendLeavingProcessDialog(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Amend Leaving Process" });

    public async Task OpenAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Amend", Exact = true }).ClickAsync();
        await Dialog.WaitForAsync(new() { Timeout = 15_000 });
        await Dialog.Locator(".e-date-wrapper input.e-input").First
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();


    private ILocator LeavingDateInput => Dialog.Locator(".e-date-wrapper input.e-input").Nth(0);

    public async Task<string?> GetLeavingDateTextAsync() => await LeavingDateInput.InputValueAsync();

    public async Task FillLeavingDateAsync(string ddMMyyyy)
    {
        await LeavingDateInput.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await LeavingDateInput.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }


    private ILocator LastWorkingDayInput => Dialog.Locator(".e-date-wrapper input.e-input").Nth(1);

    public async Task<string?> GetLastWorkingDayTextAsync() => await LastWorkingDayInput.InputValueAsync();

    public async Task FillLastWorkingDayAsync(string ddMMyyyy)
    {
        await LastWorkingDayInput.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await LastWorkingDayInput.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }


    public async Task<string?> GetLeavingReasonTextAsync() =>
        (await Dialog.Locator("span[role='combobox']").First.Locator("input").InputValueAsync())?.Trim();

    public Task SelectLeavingReasonAsync(string reasonLabel) =>
        DropDownSelector.SelectAsync(page, Dialog, reasonLabel);


    public async Task FillNotesAsync(string notes)
    {
        await Dialog.Locator("textarea").FillAsync(notes);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task<string?> GetNotesTextAsync() => await Dialog.Locator("textarea").InputValueAsync();


    public Task<bool> IsBackdatedConfirmationVisibleAsync() =>
        Dialog.Locator(".e-checkbox-wrapper").Filter(new() { HasText = "This leaving date is in the past" })
            .WaitUntilVisibleAsync(2_000);

    public Task CheckBackdatedConfirmationAsync() =>
        Dialog.Locator(".e-checkbox-wrapper").Filter(new() { HasText = "This leaving date is in the past" }).ClickAsync();


    public async Task SaveAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();

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

    public async Task CancelAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task<string?> GetErrorAsync()
    {
        var error = Dialog.Locator(".alert-danger").First;
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }
}
