using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// NFR-05: reusable assertions for modal-dialog keyboard accessibility — focus containment while a
/// dialog is open, and focus restoration to the triggering control after it closes. Used by
/// <c>DialogFocusManagementTests</c> against the Syncfusion dialogs in the app (Request Leave,
/// HrConfirmDialog, document/note upload).
/// </summary>
public static class DialogAccessibility
{
    /// <summary>
    /// Asserts the keyboard focus is currently inside <paramref name="dialog"/>, that Tabbing from
    /// the last focusable descendant wraps back to the first (and Shift+Tab from the first wraps to
    /// the last), and that focus never lands on <c>&lt;body&gt;</c> or page chrome outside the dialog.
    /// </summary>
    public static async Task AssertFocusTrappedAsync(IPage page, ILocator dialog)
    {
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        Assert.True(await IsFocusInsideAsync(page, dialog),
            "Expected keyboard focus to be inside the dialog when it opened.");

        // Walk forward through the dialog's focusable controls; focus must never escape to <body>.
        for (var i = 0; i < 25; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var tag = await page.EvaluateAsync<string>("() => document.activeElement?.tagName ?? 'BODY'");
            Assert.False(tag == "BODY" || tag == "HTML",
                "Tab moved keyboard focus onto document body — focus is not trapped within the dialog.");
            Assert.True(await IsFocusInsideAsync(page, dialog),
                "Tab moved keyboard focus outside the dialog — focus is not trapped.");
        }

        // Shift+Tab a few times — still contained.
        for (var i = 0; i < 5; i++)
        {
            await page.Keyboard.PressAsync("Shift+Tab");
            Assert.True(await IsFocusInsideAsync(page, dialog),
                "Shift+Tab moved keyboard focus outside the dialog — focus is not trapped.");
        }
    }

    /// <summary>
    /// Records the currently-focused trigger, runs <paramref name="openDialog"/>, asserts focus left
    /// <paramref name="triggerButton"/>, runs <paramref name="closeDialog"/>, then asserts focus has
    /// returned to <paramref name="triggerButton"/>.
    /// </summary>
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

        // Focus restoration can lag the close animation by a frame or two.
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
