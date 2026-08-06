using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public static class LocatorExtensions
{
    /// <summary>
    /// Non-throwing "is this visible" check that actually waits, unlike <see cref="ILocator.IsVisibleAsync"/>
    /// (which takes an instantaneous DOM snapshot with no auto-wait). Used for dialogs/elements whose
    /// appearance depends on a just-triggered client or server round trip (e.g. a Blazor Server
    /// unsaved-changes confirm dialog) — checking immediately after the triggering click is a race
    /// that reads "not visible yet" as "will never be visible", which is what made every one of these
    /// checks across the page-object suite flaky under headless/loaded runs.
    /// </summary>
    public static async Task<bool> WaitUntilVisibleAsync(this ILocator locator, int timeoutMs = 5_000)
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
}
