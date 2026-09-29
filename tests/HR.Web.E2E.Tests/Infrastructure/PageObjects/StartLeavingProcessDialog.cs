using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class StartLeavingProcessDialog(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Start Leaving Process" });

    public async Task OpenAsync()
    {
        var moreActionsButton = page.GetByRole(AriaRole.Button, new() { Name = "More actions" });
        // Id-based ("#start-offboarding", EmployeeEdit.razor's BuildMoreActionsItems), not
        // role+name — see SharedDocumentDetailPage.ClickMoreActionsItemAsync's remarks for why: an
        // id resolves against whichever render eventually wins instead of racing a role+name query
        // against the popup's item set mid-rebuild.
        var menuItem = page.Locator("#start-offboarding");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await moreActionsButton.ClickAsync();
            try
            {
                await menuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            }
            catch (TimeoutException)
            {
                await page.Keyboard.PressAsync("Escape");
                continue;
            }

            await menuItem.ClickAsync();

            try
            {
                await Dialog.WaitForAsync(new() { Timeout = 8_000 });
                return;
            }
            catch (TimeoutException)
            {
            }
        }

        await moreActionsButton.ClickAsync();
        await menuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await menuItem.ClickAsync();
        await Dialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    public async Task<string?> GetActiveStepLabelAsync()
    {
        var active = Dialog.Locator(".hr-stepper-item--current");
        var index = (await active.Locator(".hr-stepper-node").TextContentAsync())?.Trim();
        var label = (await active.Locator(".hr-stepper-label").TextContentAsync())?.Trim();
        return $"{index}. {label}";
    }


    public async Task FillResignationReceivedDateAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }


    public async Task<string?> GetLeavingDateTextAsync()
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        return (await input.InputValueAsync())?.Trim();
    }

    public async Task FillLeavingDateAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<bool> IsBackdatedConfirmationVisibleAsync() =>
        Dialog.Locator(".e-checkbox-wrapper").Filter(new() { HasText = "This leaving date is in the past" })
            .WaitUntilVisibleAsync(2_000);

    public Task CheckBackdatedConfirmationAsync() =>
        Dialog.Locator(".e-checkbox-wrapper").Filter(new() { HasText = "This leaving date is in the past" }).ClickAsync();

    public async Task ClearLeavingDateAsync()
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.Keyboard.PressAsync("Tab");
    }


    public async Task FillLastWorkingDayAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }


    public Task SelectLeavingReasonAsync(string reasonLabel) =>
        DropDownSelector.SelectAsync(page, Dialog, reasonLabel);

    public async Task FillNotesAsync(string notes)
    {
        await Dialog.Locator("textarea").FillAsync(notes);
        await page.Keyboard.PressAsync("Tab");
    }


    private ILocator ConfirmationSummary => Dialog.Locator("dl.row");

    public async Task<string?> GetConfirmationResignationReceivedDateTextAsync() =>
        (await ConfirmationSummary.Locator("dd").Nth(0).TextContentAsync())?.Trim();

    public async Task<string?> GetConfirmationLeavingDateTextAsync() =>
        (await ConfirmationSummary.Locator("dd").Nth(1).TextContentAsync())?.Trim();

    public async Task<string?> GetConfirmationLastWorkingDayTextAsync() =>
        (await ConfirmationSummary.Locator("dd").Nth(2).TextContentAsync())?.Trim();

    public async Task<string?> GetConfirmationLeavingReasonTextAsync() =>
        (await ConfirmationSummary.Locator("dd").Nth(3).TextContentAsync())?.Trim();


    public async Task ClickNextAsync()
    {
        var activeStepLocator = Dialog.Locator(".hr-stepper-item--current");
        var beforeLabel = (await activeStepLocator.TextContentAsync())?.Trim() ?? string.Empty;

        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

        try
        {
            await Assertions.Expect(activeStepLocator).Not.ToHaveTextAsync(beforeLabel, new() { Timeout = 8_000 });
        }
        catch (PlaywrightException)
        {
            await Dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
    }

    public Task ClickBackAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();

    public async Task ConfirmAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm" }).ClickAsync();

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

    public async Task<string?> GetStepErrorAsync()
    {
        var error = Dialog.Locator(".alert-danger").First;
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }

    public Task<string?> GetGlobalErrorAsync() => GetStepErrorAsync();
}
