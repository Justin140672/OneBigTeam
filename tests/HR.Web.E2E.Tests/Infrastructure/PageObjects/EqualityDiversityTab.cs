using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

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

    public async Task<string> GetIntroTextAsync() =>
        (await page.Locator(".ed-intro").InnerTextAsync()).Trim();

    private ILocator FieldGroup(string testId) =>
        page.Locator($"[data-testid='{testId}']");

    public Task SelectAsync(string fieldTestId, string optionText) =>
        DropDownSelector.SelectAsync(page, FieldGroup(fieldTestId), optionText);

    public async Task<string> GetSelectedValueAsync(string fieldTestId) =>
        (await FieldGroup(fieldTestId).Locator("span[role='combobox'] input").First.InputValueAsync())?.Trim() ?? "";

    public async Task SaveAsync()
    {
        var banner = page.Locator("[data-testid='my-profile-equality-success']");
        if (await banner.CountAsync() > 0)
        {
            await banner.Locator(".ed-success-dismiss").ClickAsync();
            await banner.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        }

        await page.Locator("[data-testid='my-profile-equality-save']").ClickAsync();
        await Assertions.Expect(banner.Locator(".ed-success-title"))
            .ToContainTextAsync("saved", new() { IgnoreCase = true, Timeout = 15_000 });
    }

    public async Task<bool> IsSuccessBannerVisibleAsync() =>
        await page.Locator("[data-testid='my-profile-equality-success']").IsVisibleAsync();

    public async Task<string> GetSuccessBannerTextAsync() =>
        (await page.Locator("[data-testid='my-profile-equality-success'] .ed-success-title").InnerTextAsync()).Trim();

    public async Task ClearAnswersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear my answers" }).ClickAsync();

        await Assertions.Expect(page.Locator("[data-testid='my-profile-equality-success'] .ed-success-title"))
            .ToContainTextAsync("cleared", new() { IgnoreCase = true, Timeout = 15_000 });
    }

    public void AcceptConfirmDialogs() =>
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
}
