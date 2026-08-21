using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the blocking "Complete your employee profile" dialog
/// (EmployeeCompletionDialog.razor), shown by MainLayout.razor whenever the current session's
/// employee record still has RequiresInitialSetup = true. Rendered as an SfDialog with
/// ShowCloseIcon="false"/CloseOnEscape="false" and no overlay-click dismissal — it is the ONLY
/// thing rendered while blocking (no sidebar/topbar/@Body), so most locators here are scoped to
/// ".employee-completion-dialog" defensively even though nothing else on the page should collide.
///
/// Field selectors are placeholder-based (HrTextBox renders FloatLabelType.Never by default
/// specifically so GetByPlaceholder works — see HrTextBox.cs's own remarks) except for the two
/// Syncfusion dropdowns (Nationality/Gender), which always go through the shared DropDownSelector
/// helper per this suite's convention.
/// </summary>
public sealed class EmployeeCompletionDialogPage(IPage page)
{
    private ILocator Dialog => page.Locator(".employee-completion-dialog");

    public async Task WaitForVisibleAsync() =>
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    public Task<bool> IsHeaderVisibleAsync() =>
        page.GetByRole(AriaRole.Heading, new() { Name = "Complete your employee profile" }).IsVisibleAsync();

    // ── Field fill helpers ─────────────────────────────────────────────────────

    public Task FillFirstNameAsync(string value) => Dialog.GetByPlaceholder("First name").FillAsync(value);

    public Task FillLastNameAsync(string value) => Dialog.GetByPlaceholder("Last name").FillAsync(value);

    public Task FillPreferredNameAsync(string value) => Dialog.GetByPlaceholder("Defaults to first name").FillAsync(value);

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

    public Task FillGenderOtherAsync(string value) => Dialog.GetByPlaceholder("Please specify").FillAsync(value);

    public Task FillPersonalEmailAsync(string value) => Dialog.GetByPlaceholder("personal@example.com").FillAsync(value);

    public Task FillPhoneNumberAsync(string value) => Dialog.GetByPlaceholder("e.g. 07700 900000").FillAsync(value);

    public Task FillHomePhoneAsync(string value) => Dialog.GetByPlaceholder("e.g. 01234 567890").FillAsync(value);

    public Task FillAddressLine1Async(string value) => Dialog.GetByPlaceholder("Street address").FillAsync(value);

    public Task FillAddressLine2Async(string value) => Dialog.GetByPlaceholder("Apartment, suite, etc.").FillAsync(value);

    public Task FillCityAsync(string value) => Dialog.GetByPlaceholder("e.g. London").FillAsync(value);

    public Task FillCountyAsync(string value) => Dialog.GetByPlaceholder("e.g. Greater London").FillAsync(value);

    public Task FillPostcodeAsync(string value) => Dialog.GetByPlaceholder("e.g. SW1A 1AA").FillAsync(value);

    public Task FillCountryAsync(string value) => Dialog.GetByPlaceholder("e.g. United Kingdom").FillAsync(value);

    /// <summary>
    /// Fills every required field with valid values (plus a fixed Nationality/Gender selection),
    /// leaving optional fields untouched. Callers that need to test a specific missing/invalid
    /// field should fill individually instead.
    /// </summary>
    public async Task FillAllRequiredFieldsAsync(
        string firstName, string lastName, string dobDdMMyyyy,
        string nationality, string gender,
        string addressLine1, string city, string postcode)
    {
        await FillFirstNameAsync(firstName);
        await FillLastNameAsync(lastName);
        await FillDateOfBirthAsync(dobDdMMyyyy);
        await SelectNationalityAsync(nationality);
        await SelectGenderAsync(gender);
        await FillAddressLine1Async(addressLine1);
        await FillCityAsync(city);
        await FillPostcodeAsync(postcode);
    }

    // ── Actions ─────────────────────────────────────────────────────────────────

    public Task ClickSaveAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Save and continue" }).ClickAsync();

    /// <summary>Clicks Save and waits for the dialog to close (happy-path only).</summary>
    public async Task SaveAndWaitForCloseAsync()
    {
        await ClickSaveAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }

    /// <summary>Clicks Save without waiting for any outcome — for validation-failure assertions.</summary>
    public async Task ClickSaveExpectingValidationFailureAsync()
    {
        await ClickSaveAsync();
        await page.WaitForTimeoutAsync(1_000);
    }

    public Task ClickLogoutAsync() => Dialog.GetByRole(AriaRole.Link, new() { Name = "Log out" }).ClickAsync();

    public Task<bool> TryDismissViaEscapeAsync() => TryDismissAsync(async () => await page.Keyboard.PressAsync("Escape"));

    public Task<bool> TryDismissViaOutsideClickAsync() => TryDismissAsync(async () =>
        // Click at a corner of the viewport, well outside the modal content.
        await page.Mouse.ClickAsync(2, 2));

    private async Task<bool> TryDismissAsync(Func<Task> dismissAttempt)
    {
        await dismissAttempt();
        await page.WaitForTimeoutAsync(500);
        return await Dialog.IsVisibleAsync();
    }

    // ── Validation assertions ────────────────────────────────────────────────────

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

    // Scopes to the field's own column wrapper via its <label> text, matching the convention used
    // throughout this suite for disambiguating multiple Syncfusion comboboxes on the same
    // form/dialog (e.g. AmendLeavingProcessDialog, PositionProfileEditPage).
    private ILocator FieldGroup(string labelText) =>
        Dialog.Locator(".col-md-6").Filter(new() { Has = page.Locator("label", new() { HasText = labelText }) }).First;
}
