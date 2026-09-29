using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class PersonalDetailsTab(IPage page)
{
    public async Task WaitForLoadAsync() =>
        await page.WaitForSelectorAsync(".pd-card, .alert", new() { Timeout = 15_000 });

    public async Task<bool> IsVisibleAsync() =>
        await page.Locator(".pd-card").IsVisibleAsync();


    public async Task<string?> GetDetailAsync(string label)
    {
        var dt = page.Locator(".pd-dl dt").Filter(new() { HasText = label }).First;
        try
        {
            await dt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await dt.Locator("~ dd").First.TextContentAsync())?.Trim();
    }


    public async Task ClickRequestChangeAsync()
    {
        await page.Locator("button.pd-change-btn").ClickAsync();
        await page.WaitForSelectorAsync(".e-dialog", new() { Timeout = 10_000 });
    }

    public async Task FillChangeRequestNotesAsync(string notes)
    {
        var textarea = page.Locator("textarea#pd-notes");
        await textarea.ClearAsync();
        await textarea.FillAsync(notes);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SubmitChangeRequestAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-dialog",
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        await page.WaitForSelectorAsync(".pd-success-banner", new() { Timeout = 5_000 });
    }

    public async Task ClickSubmitRequestAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();
        await page.WaitForTimeoutAsync(1_000);
    }

    public async Task CancelChangeRequestAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task<bool> IsSuccessBannerVisibleAsync() =>
        await page.Locator(".pd-success-banner").IsVisibleAsync();

    public async Task<bool> IsDialogOpenAsync() =>
        await page.Locator(".e-dialog").IsVisibleAsync();

    public async Task<bool> HasValidationErrorAsync()
    {
        try
        {
            await page.Locator(".is-invalid").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
