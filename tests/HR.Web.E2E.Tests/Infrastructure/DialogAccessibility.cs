using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public static class DialogAccessibility
{
    public static async Task AssertFocusTrappedAsync(IPage page, ILocator dialog)
    {
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var focusInsideOnOpen = false;
        var openFocusDeadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < openFocusDeadline)
        {
            focusInsideOnOpen = await IsFocusInsideAsync(page, dialog);
            if (focusInsideOnOpen) break;
            await page.WaitForTimeoutAsync(100);
        }
        Assert.True(focusInsideOnOpen,
            "Expected keyboard focus to be inside the dialog when it opened.");

        for (var i = 0; i < 25; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var tag = await page.EvaluateAsync<string>("() => document.activeElement?.tagName ?? 'BODY'");
            Assert.False(tag == "BODY" || tag == "HTML",
                "Tab moved keyboard focus onto document body — focus is not trapped within the dialog.");
            Assert.True(await IsFocusInsideAsync(page, dialog),
                "Tab moved keyboard focus outside the dialog — focus is not trapped.");
        }

        for (var i = 0; i < 5; i++)
        {
            await page.Keyboard.PressAsync("Shift+Tab");
            Assert.True(await IsFocusInsideAsync(page, dialog),
                "Shift+Tab moved keyboard focus outside the dialog — focus is not trapped.");
        }
    }

    public static async Task AssertFocusRestoredAsync(
        IPage page,
        Func<Task> openDialog,
        Func<Task> closeDialog,
        ILocator triggerButton)
    {
        await triggerButton.FocusAsync();
        Assert.True(await triggerButton.EvaluateAsync<bool>("el => el === document.activeElement"),
            "Precondition failed: the trigger button could not be focused before opening the dialog.");

        await openDialog();

        Assert.False(await triggerButton.EvaluateAsync<bool>("el => el === document.activeElement"),
            "Expected keyboard focus to move off the trigger button and into the dialog on open.");

        await closeDialog();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        var restored = false;
        while (DateTime.UtcNow < deadline)
        {
            restored = await triggerButton.EvaluateAsync<bool>("el => el === document.activeElement");
            if (restored) break;
            await page.WaitForTimeoutAsync(100);
        }

        Assert.True(restored,
            "Expected keyboard focus to return to the trigger button after the dialog closed.");
    }

    private static Task<bool> IsFocusInsideAsync(IPage page, ILocator dialog) =>
        dialog.EvaluateAsync<bool>(
            "el => el.contains(document.activeElement) || el === document.activeElement");
}
