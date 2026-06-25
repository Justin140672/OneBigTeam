using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Personal Details tab on the self-service My Profile page.
/// Selectors derived from MyProfilePersonalDetailsTab.razor (.pd-* CSS classes).
/// </summary>
public sealed class PersonalDetailsTab(IPage page)
{
    public async Task WaitForLoadAsync() =>
        await page.WaitForSelectorAsync(".pd-card, .alert", new() { Timeout = 15_000 });

    public async Task<bool> IsVisibleAsync() =>
        await page.Locator(".pd-card").IsVisibleAsync();

    // ── Data display ───────────────────────────────────────────────────────────

    /// <summary>Returns the displayed value for a dt label in the personal details definition list.</summary>
    public async Task<string?> GetDetailAsync(string label)
    {
        var dt = page.Locator(".pd-dl dt").Filter(new() { HasText = label }).First;
        if (!await dt.IsVisibleAsync()) return null;
        return (await dt.Locator("~ dd").First.TextContentAsync())?.Trim();
    }

    // ── Request Change dialog ──────────────────────────────────────────────────

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
    }

    /// <summary>
    /// Clicks Submit and waits for the dialog to close and the success banner to appear.
    /// Use this for the happy path only — it will time out if validation keeps the dialog open.
    /// </summary>
    public async Task SubmitChangeRequestAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-dialog",
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        // Wait for the success banner to appear in the same render cycle.
        await page.WaitForSelectorAsync(".pd-success-banner", new() { Timeout = 5_000 });
    }

    /// <summary>
    /// Clicks Submit without waiting for any outcome.
    /// Use this when testing validation failures where the dialog stays open.
    /// </summary>
    public async Task ClickSubmitRequestAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();
        // Brief pause to allow client-side validation to render.
        await page.WaitForTimeoutAsync(1_000);
    }

    public async Task CancelChangeRequestAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task<bool> IsSuccessBannerVisibleAsync() =>
        await page.Locator(".pd-success-banner").IsVisibleAsync();

    /// <summary>Returns true if the dialog is currently open.</summary>
    public async Task<bool> IsDialogOpenAsync() =>
        await page.Locator(".e-dialog").IsVisibleAsync();

    public async Task<bool> HasValidationErrorAsync() =>
        await page.Locator(".is-invalid").First.IsVisibleAsync();
}
