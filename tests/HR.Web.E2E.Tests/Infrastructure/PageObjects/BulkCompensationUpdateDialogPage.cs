using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Bulk Update dialog opened from the Employee List toolbar
/// (Components/Pages/Employees/BulkCompensationUpdateDialog.razor), which wraps
/// BulkCompensationAdjustmentPanel.razor (SectionHeading="Adjustment Details") in an SfDialog.
///
/// All locators are scoped to the dialog element itself (identified by its own CssClass,
/// "bulk-compensation-update-dialog", combined with role='dialog' to avoid matching the close
/// button/outer container Syncfusion also stamps with the same CssClass) so they can't collide
/// with same-named controls on the underlying Employee List page or elsewhere.
/// </summary>
public sealed class BulkCompensationUpdateDialogPage(IPage page)
{
    private ILocator Dialog => page.Locator("[role='dialog'].bulk-compensation-update-dialog");

    public Task<bool> IsOpenAsync() => Dialog.IsVisibleAsync();

    public Task<string?> GetSelectedEmployeesSummaryAsync() =>
        Dialog.Locator("p.text-muted").First.TextContentAsync();


    public Task SelectModeAsync(string modeLabel) =>
        DropDownSelector.SelectAsync(page, Dialog, modeLabel);

    public Task SelectReasonAsync(string reasonLabel) =>
        DropDownSelector.SelectAsync(page, Dialog, reasonLabel, index: 1);

    public async Task FillAdjustmentValueAsync(string value)
    {
        var input = Dialog.Locator("input.e-numerictextbox").First;
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillEffectiveDateAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillNotesAsync(string value)
    {
        await Dialog.Locator("textarea").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task ClickBuildPreviewAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Build Preview", Exact = true }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[role='dialog'].bulk-compensation-update-dialog h5:has-text('Preview & Confirm'), " +
            "[role='dialog'].bulk-compensation-update-dialog .alert-danger",
            new() { Timeout = 15_000 });
    }


    private ILocator PreviewCard => Dialog.Locator(".card", new() { HasText = "Preview & Confirm" });

    public Task<bool> HasPreviewCardAsync() => PreviewCard.IsVisibleAsync();

    public async Task<int> GetPreviewRowCountAsync()
    {
        await page.WaitForSelectorAsync(
            "[role='dialog'].bulk-compensation-update-dialog .e-grid .e-row, " +
            "[role='dialog'].bulk-compensation-update-dialog .e-grid .e-emptyrow",
            new() { Timeout = 15_000 });
        return await PreviewCard.Locator(".e-grid .e-row").CountAsync();
    }

    public ILocator PreviewRow(string employeeNameFragment) =>
        PreviewCard.Locator(".e-grid .e-row").Filter(new() { HasText = employeeNameFragment });

    public async Task<decimal> GetProposedSalaryAsync(string employeeNameFragment)
    {
        var row = PreviewRow(employeeNameFragment).First;
        var value = await row.Locator("input.e-numerictextbox").First.InputValueAsync();
        return decimal.Parse(value);
    }

    public async Task<string?> GetExcludedEmployeesTextAsync()
    {
        var banner = Dialog.Locator(".alert-warning");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    public async Task SetProposedSalaryAsync(string employeeNameFragment, string value)
    {
        var row = PreviewRow(employeeNameFragment).First;
        var input = row.Locator("input.e-numerictextbox").First;
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");

        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value, new() { Delay = 50 });
        await page.Keyboard.PressAsync("Tab");

        var digits = value.TrimStart('-').TrimEnd();
        var pattern = "^-?" +
            string.Join(",?", digits.Select(c => System.Text.RegularExpressions.Regex.Escape(c.ToString()))) +
            @"(\.\d+)?$";
        await Assertions.Expect(input).ToHaveValueAsync(
            new System.Text.RegularExpressions.Regex(pattern),
            new() { Timeout = 10_000 });
    }

    public async Task ConfirmApplyAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm Apply", Exact = true }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<string?> GetGlobalErrorAsync()
    {
        var banner = Dialog.Locator(".alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    public Task ClickCloseAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
}
