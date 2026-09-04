using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the self-service "Equality &amp; Diversity" tab on the My Profile page
/// (src/HR.Web/Components/Pages/Employees/MyProfileEqualityDiversityTab.razor).
///
/// Field groups each carry a data-testid: my-profile-equality-{gender|marital|ethnicgroup|
/// disability|orientation|religion|caring}. Save button data-testid=my-profile-equality-save; success
/// banner data-testid=my-profile-equality-success. "Clear my answers" goes through a native
/// window.confirm — register a page Dialog handler (see AcceptConfirmDialogs) before calling
/// <see cref="ClearAnswersAsync"/>.
/// </summary>
public sealed class EqualityDiversityTab(IPage page)
{
    public const string GenderField      = "my-profile-equality-gender";
    public const string MaritalField     = "my-profile-equality-marital";
    public const string EthnicGroupField = "my-profile-equality-ethnicgroup";
    public const string DisabilityField  = "my-profile-equality-disability";
    public const string OrientationField = "my-profile-equality-orientation";
    public const string ReligionField    = "my-profile-equality-religion";
    public const string CaringField      = "my-profile-equality-caring";

    public const string ClearConfirmText =
        "Clear all of your equality and diversity answers? This cannot be undone.";

    public async Task WaitForLoadAsync() =>
        await page.WaitForSelectorAsync("[data-testid='my-profile-equality-section'], .alert-danger",
            new() { Timeout = 15_000 });

    public async Task<bool> IsSectionVisibleAsync() =>
        await page.Locator("[data-testid='my-profile-equality-section']").IsVisibleAsync();

    /// <summary>The full trimmed text of the explanatory intro block.</summary>
    public async Task<string> GetIntroTextAsync() =>
        (await page.Locator(".ed-intro").InnerTextAsync()).Trim();

    private ILocator FieldGroup(string testId) =>
        page.Locator($"[data-testid='{testId}']");

    /// <summary>Selects an option in one of the questionnaire dropdowns via the shared selector.</summary>
    public Task SelectAsync(string fieldTestId, string optionText) =>
        DropDownSelector.SelectAsync(page, FieldGroup(fieldTestId), optionText);

    /// <summary>Reads the currently-selected label from a questionnaire dropdown's combobox input.</summary>
    public async Task<string> GetSelectedValueAsync(string fieldTestId) =>
        (await FieldGroup(fieldTestId).Locator("span[role='combobox'] input").First.InputValueAsync())?.Trim() ?? "";

    public async Task SaveAsync()
    {
        await page.Locator("[data-testid='my-profile-equality-save']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='my-profile-equality-success']",
            new() { Timeout = 15_000 });
    }

    public async Task<bool> IsSuccessBannerVisibleAsync() =>
        await page.Locator("[data-testid='my-profile-equality-success']").IsVisibleAsync();

    public async Task<string> GetSuccessBannerTextAsync() =>
        (await page.Locator("[data-testid='my-profile-equality-success'] .ed-success-title").InnerTextAsync()).Trim();

    /// <summary>
    /// Clicks "Clear my answers" and waits for the success banner. The native confirm dialog must
    /// already be auto-accepted by a registered page Dialog handler
    /// (see <see cref="AcceptConfirmDialogs"/>).
    /// </summary>
    public async Task ClearAnswersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear my answers" }).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='my-profile-equality-success']",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Registers a page Dialog handler that accepts every native dialog (window.confirm). Playwright
    /// auto-dismisses dialogs unless a handler is attached, which would cancel the clear.
    /// </summary>
    public void AcceptConfirmDialogs() =>
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
}
