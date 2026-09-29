using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class EmergencyContactsTab(IPage page)
{
    public async Task WaitForLoadAsync() =>
        await page.WaitForSelectorAsync(".ec-card, .alert", new() { Timeout = 15_000 });


    public async Task ClickAddContactAsync()
    {
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Add Contact" }).First;
        await btn.ClickAsync();
        await page.WaitForSelectorAsync("input[placeholder='Full name']", new() { Timeout = 10_000 });
    }

    public async Task FillContactNameAsync(string name)
    {
        await page.GetByPlaceholder("Full name").FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillContactRelationshipAsync(string relationship)
    {
        await page.GetByPlaceholder("e.g. Spouse, Parent, Sibling").FillAsync(relationship);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillContactPhoneAsync(string phone)
    {
        await page.GetByPlaceholder("e.g. 07700 900000").Last.FillAsync(phone);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillContactEmailAsync(string email)
    {
        await page.GetByPlaceholder("e.g. name@example.com").FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SaveContactAsync()
    {
        var saveBtn = page.Locator("button.e-primary, button[type='submit']")
            .Filter(new() { HasText = "Add Contact" })
            .Last;
        await saveBtn.ClickAsync();
        await page.WaitForSelectorAsync(".ec-success-banner", new() { Timeout = 15_000 });
    }

    public async Task ClickSaveContactAsync()
    {
        var saveBtn = page.Locator("button.e-primary, button[type='submit']")
            .Filter(new() { HasText = "Add Contact" })
            .Last;
        await saveBtn.ClickAsync();
    }

    public async Task<bool> HasValidationMessageAsync()
    {
        try
        {
            await page.Locator(".validation-message").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }


    public Task<bool> HasContactAsync(string nameFragment) =>
        page.Locator(".ec-contact-name")
            .Filter(new() { HasText = nameFragment })
            .First
            .WaitUntilVisibleAsync();

    public async Task<IReadOnlyList<string>> GetContactNamesAsync()
    {
        var items = await page.Locator(".ec-contact-name").AllAsync();
        var names = new List<string>();
        foreach (var item in items)
            names.Add((await item.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task<bool> IsEmptyStateVisibleAsync() =>
        await page.GetByText("No emergency contacts added yet.").IsVisibleAsync();

    public async Task<bool> IsSuccessBannerVisibleAsync() =>
        await page.Locator(".ec-success-banner").IsVisibleAsync();


    public async Task ClickEditContactAsync(string nameFragment)
    {
        var card = page.Locator(".ec-contact-card")
            .Filter(new() { HasText = nameFragment })
            .First;
        await card.Locator("button[title='Edit']").ClickAsync();
        await page.WaitForSelectorAsync("input[placeholder='Full name']", new() { Timeout = 10_000 });
    }

    public async Task ClickRemoveContactAsync(string nameFragment)
    {
        var card = page.Locator(".ec-contact-card")
            .Filter(new() { HasText = nameFragment })
            .First;
        await card.Locator("button[title='Remove']").ClickAsync();
        await page.WaitForSelectorAsync(".ec-success-banner", new() { Timeout = 15_000 });
    }
}
