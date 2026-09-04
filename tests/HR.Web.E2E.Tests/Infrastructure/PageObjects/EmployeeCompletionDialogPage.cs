using System.Text.RegularExpressions;
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
    // Syncfusion's SfDialog stamps CssClass onto BOTH the outer ".e-dlg-container" positioning
    // wrapper AND the inner ".e-dialog[role=dialog]" element, so a bare ".employee-completion-dialog"
    // matches two nodes and trips Playwright strict mode. Scope to the actual dialog element.
    private ILocator Dialog => page.Locator("[role='dialog'].employee-completion-dialog");

    public async Task WaitForVisibleAsync() =>
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    private ILocator Heading =>
        page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("let's complete your profile") });

    public Task<bool> IsHeaderVisibleAsync() => Heading.IsVisibleAsync();

    public Task<string> HeadingTextAsync() => Heading.InnerTextAsync();

    /// <summary>The redesigned personalised welcome heading includes the account first name.</summary>
    public Task<bool> HeadingShowsWelcomeForAsync(string firstName) =>
        page.GetByRole(AriaRole.Heading,
                new() { NameRegex = new Regex($"Welcome, {Regex.Escape(firstName)} — let's complete your profile") })
            .IsVisibleAsync();

    /// <summary>The supporting explanatory paragraph shown under the heading.</summary>
    public Task<bool> SupportingTextVisibleAsync() =>
        Dialog.GetByText("Please add the remaining information below so we can finish setting up your employee account")
            .IsVisibleAsync();

    // ── Read-only name display ─────────────────────────────────────────────────

    private ILocator ReadOnlyFirstName => Dialog.Locator("[aria-labelledby='ecd-firstname-label'].ecd-readonly");
    private ILocator ReadOnlyLastName => Dialog.Locator("[aria-labelledby='ecd-lastname-label'].ecd-readonly");

    public Task<string> ReadOnlyFirstNameText() => ReadOnlyFirstName.InnerTextAsync();

    public Task<string> ReadOnlyLastNameText() => ReadOnlyLastName.InnerTextAsync();

    /// <summary>
    /// First/last name are rendered as static ".ecd-readonly" display text (role="text"), not
    /// inputs. "Editable" means an actual &lt;input&gt;/&lt;textarea&gt; carrying the field's own
    /// label association exists — checked against the label id rather than a placeholder, since the
    /// Preferred Name input's placeholder ("Defaults to first name") contains "first name" and
    /// Playwright's GetByPlaceholder is a case-insensitive substring match.
    /// </summary>
    public async Task<bool> IsFirstNameEditable() =>
        await Dialog.Locator("input[aria-labelledby='ecd-firstname-label'], textarea[aria-labelledby='ecd-firstname-label'], input#ecd-firstname, textarea#ecd-firstname").CountAsync() > 0;

    public async Task<bool> IsLastNameEditable() =>
        await Dialog.Locator("input[aria-labelledby='ecd-lastname-label'], textarea[aria-labelledby='ecd-lastname-label'], input#ecd-lastname, textarea#ecd-lastname").CountAsync() > 0;

    public Task<bool> NameCorrectionNoteVisibleAsync() =>
        Dialog.GetByText("Need to correct your name? Contact your HR administrator after setup.").IsVisibleAsync();

    public Task<bool> HasSectionHeadingAsync(string text) =>
        Dialog.GetByRole(AriaRole.Heading, new() { Name = text, Level = 3 }).IsVisibleAsync();

    // ── Field fill helpers ─────────────────────────────────────────────────────

    // HrTextBox (SfTextBox) only raises ValueChanged — and therefore only pushes the typed text
    // into the EditForm model that submit-time validation reads — on the "change"/blur event, NOT
    // on FillAsync's raw "input" event. Without an explicit blur after each fill the dialog
    // submits with those fields still empty (looks like "nothing was typed"), so every text field
    // here goes through this helper. (Nationality/Gender are SfDropDownList — handled separately;
    // Date of Birth has its own Escape-to-commit dance below.)
    private async Task FillFieldAsync(string placeholder, string value)
    {
        var field = Dialog.GetByPlaceholder(placeholder);
        await field.FillAsync(value);
        // Tab (not a bare .blur()) to commit: stock SfTextBox only raises ValueChanged on the
        // native "change" event, and a real focus move is the reliable way to fire it — matching
        // every other page object in this suite. A programmatic element.blur() does not always
        // produce "change" for a Syncfusion-wrapped input.
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

    // Country is not shown on the completion dialog (UK-only for now — Model.Country defaults to
    // "United Kingdom").

    /// <summary>
    /// Fills every required field with valid values (plus a fixed Nationality/Gender selection),
    /// leaving optional fields untouched. Callers that need to test a specific missing/invalid
    /// field should fill individually instead.
    /// </summary>
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

    // ── Actions ─────────────────────────────────────────────────────────────────

    public Task ClickSaveAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Complete setup" }).ClickAsync();

    public Task<bool> IsPrimaryButtonLabelledCompleteSetupAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Complete setup" }).IsVisibleAsync();

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
