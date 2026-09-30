using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class EmployeeCompletionDialogPage(IPage page)
{
    private ILocator Dialog => page.Locator("[role='dialog'].employee-completion-dialog");

    public async Task WaitForVisibleAsync() =>
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    private ILocator Heading =>
        page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("let's complete your profile") });

    public Task<bool> IsHeaderVisibleAsync() => Heading.IsVisibleAsync();

    public Task<string> HeadingTextAsync() => Heading.InnerTextAsync();

    public Task<bool> HeadingShowsWelcomeForAsync(string firstName) =>
        page.GetByRole(AriaRole.Heading,
                new() { NameRegex = new Regex($"Welcome, {Regex.Escape(firstName)} — let's complete your profile") })
            .IsVisibleAsync();

    public Task<bool> SupportingTextVisibleAsync() =>
        Dialog.GetByText("Please add the remaining information below so we can finish setting up your employee account")
            .IsVisibleAsync();


    private ILocator ReadOnlyFirstName => Dialog.Locator("[aria-labelledby='ecd-firstname-label'].ecd-readonly");
    private ILocator ReadOnlyLastName => Dialog.Locator("[aria-labelledby='ecd-lastname-label'].ecd-readonly");

    public Task<string> ReadOnlyFirstNameText() => ReadOnlyFirstName.InnerTextAsync();

    public Task<string> ReadOnlyLastNameText() => ReadOnlyLastName.InnerTextAsync();

    public async Task<bool> IsFirstNameEditable() =>
        await Dialog.Locator("input[aria-labelledby='ecd-firstname-label'], textarea[aria-labelledby='ecd-firstname-label'], input#ecd-firstname, textarea#ecd-firstname").CountAsync() > 0;

    public async Task<bool> IsLastNameEditable() =>
        await Dialog.Locator("input[aria-labelledby='ecd-lastname-label'], textarea[aria-labelledby='ecd-lastname-label'], input#ecd-lastname, textarea#ecd-lastname").CountAsync() > 0;

    public Task<bool> NameCorrectionNoteVisibleAsync() =>
        Dialog.GetByText("Need to correct your name? Contact your HR administrator after setup.").IsVisibleAsync();

    public Task<bool> HasSectionHeadingAsync(string text) =>
        Dialog.GetByRole(AriaRole.Heading, new() { Name = text, Level = 3 }).IsVisibleAsync();


    private async Task FillFieldAsync(string placeholder, string value)
    {
        var field = Dialog.GetByPlaceholder(placeholder);
        await field.FillAsync(value);
        await field.PressAsync("Tab");
    }

    public Task FillPreferredNameAsync(string value) => FillFieldAsync("Defaults to first name", value);

    public async Task FillDateOfBirthAsync(string ddMMyyyy)
    {
        var input = Dialog.GetByPlaceholder("dd/mm/yyyy");
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Escape");
    }

    public Task SelectNationalityAsync(string text) =>
        DropDownSelector.SelectAsync(page, FieldGroup("Nationality"), text);

    public Task SelectGenderAsync(string text) =>
        DropDownSelector.SelectAsync(page, FieldGroup("Gender"), text);

    public Task FillGenderOtherAsync(string value) => FillFieldAsync("Please specify", value);

    public Task FillPersonalEmailAsync(string value) => FillFieldAsync("personal@example.com", value);

    public Task FillPhoneNumberAsync(string value) => FillFieldAsync("e.g. 07700 900000", value);

    public Task FillHomePhoneAsync(string value) => FillFieldAsync("e.g. 01234 567890", value);

    public Task FillAddressLine1Async(string value) => FillFieldAsync("Street address", value);

    public Task FillAddressLine2Async(string value) => FillFieldAsync("Apartment, suite, etc.", value);

    public Task FillCityAsync(string value) => FillFieldAsync("e.g. London", value);

    public Task FillCountyAsync(string value) => FillFieldAsync("e.g. Greater London", value);

    public Task FillPostcodeAsync(string value) => FillFieldAsync("e.g. SW1A 1AA", value);


    public async Task FillAllRequiredFieldsAsync(
        string dobDdMMyyyy,
        string nationality, string gender,
        string addressLine1, string city, string postcode)
    {
        await FillDateOfBirthAsync(dobDdMMyyyy);
        await SelectNationalityAsync(nationality);
        await SelectGenderAsync(gender);
        await FillAddressLine1Async(addressLine1);
        await FillCityAsync(city);
        await FillPostcodeAsync(postcode);
    }


    public Task ClickSaveAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Complete setup" }).ClickAsync();

    public Task<bool> IsPrimaryButtonLabelledCompleteSetupAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Complete setup" }).IsVisibleAsync();

    public async Task SaveAndWaitForCloseAsync()
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (attempt > 1 && !await Dialog.IsVisibleAsync())
                return;

            await ClickSaveAsync();
            try
            {
                await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = attempt < 3 ? 8_000 : 20_000 });
                return;
            }
            catch (TimeoutException) when (attempt < 3)
            {
            }
        }
    }

    public async Task ClickSaveExpectingValidationFailureAsync()
    {
        await ClickSaveAsync();
        await page.WaitForTimeoutAsync(1_000);
    }

    public Task ClickLogoutAsync() => Dialog.GetByRole(AriaRole.Link, new() { Name = "Log out" }).ClickAsync();

    public Task<bool> TryDismissViaEscapeAsync() => TryDismissAsync(async () => await page.Keyboard.PressAsync("Escape"));

    public Task<bool> TryDismissViaOutsideClickAsync() => TryDismissAsync(async () =>
        await page.Mouse.ClickAsync(2, 2));

    private async Task<bool> TryDismissAsync(Func<Task> dismissAttempt)
    {
        await dismissAttempt();
        await page.WaitForTimeoutAsync(500);
        return await Dialog.IsVisibleAsync();
    }


    public async Task<bool> HasValidationErrorAsync(string messageFragment)
    {
        try
        {
            await Dialog.Locator(".validation-message, .field-validation-error")
                .Filter(new() { HasText = messageFragment })
                .First
                .WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public Task<bool> HasAnyValidationErrorAsync() =>
        Dialog.Locator(".validation-message, .field-validation-error").First.IsVisibleAsync();

    private ILocator FieldGroup(string labelText) =>
        Dialog.Locator(".col-md-6").Filter(new() { Has = page.Locator("label", new() { HasText = labelText }) }).First;
}
