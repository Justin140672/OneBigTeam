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

    /// <summary>
    /// Waits out a Blazor Server "busy" round trip after clicking a save/confirm/deactivate button,
    /// without the classic race of checking "spinner gone" as the only condition. A bare
    /// WaitForFunctionAsync("!spinner || !visible") can resolve the instant it's called if the
    /// spinner's own render patch hasn't reached the browser yet over SignalR — i.e. it reads
    /// "hasn't started" as "already finished", and callers move on to assert new state before the
    /// server has actually applied it. Waiting for the spinner to appear first (tolerating it never
    /// showing, for round trips fast enough to skip a visible frame) then waiting for it to clear
    /// closes that gap.
    /// </summary>
    public static async Task WaitForSpinnerToClearAsync(this IPage page, int appearTimeoutMs = 2_000, int clearTimeoutMs = 15_000)
    {
        try
        {
            await page.Locator(".spinner-border").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = appearTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Round trip completed before a spinner frame ever rendered — nothing to wait out.
        }

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = clearTimeoutMs });
    }
}
