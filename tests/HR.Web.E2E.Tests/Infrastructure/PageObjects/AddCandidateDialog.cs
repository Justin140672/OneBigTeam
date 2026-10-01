using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the vacancy Applications tab's "Add Candidate" dialog
/// (VacancyApplicationsTab.razor, internal recruitment Ticket 3): "Select existing candidate" /
/// "Create new candidate" modes, the optional new-candidate CV upload, the "Attach current CV"
/// checkbox, and the duplicate-email follow-up.
///
/// Every locator is scoped to the dialog itself via "[role='dialog'].add-application-dialog" (never
/// the bare CssClass — Syncfusion puts it on several nodes) and to data-testid field wrappers. The
/// Source dropdown is the dialog's 2nd combobox in existing mode but its 1st in new mode, so it's
/// always driven through its own wrapper rather than a dialog-wide combobox index.
///
/// Opening the dialog is <see cref="VacancyDetailPage.ClickAddCandidateAsync"/>; the older
/// VacancyDetailPage Add-dialog helpers remain valid for the existing-candidate flow.
/// </summary>
public sealed class AddCandidateDialog(IPage page)
{
    public const string SuccessMessage = "Candidate added to vacancy.";

    public ILocator Dialog => page.Locator("[role='dialog'].add-application-dialog");

    private ILocator ModeExistingButton => Dialog.Locator("[data-testid='add-candidate-mode-existing']");
    private ILocator ModeNewButton => Dialog.Locator("[data-testid='add-candidate-mode-new']");

    private ILocator CandidateField => Dialog.Locator("[data-testid='add-application-candidate-field']");
    private ILocator NewCandidateFields => Dialog.Locator("[data-testid='new-candidate-fields']");
    private ILocator SourceField => Dialog.Locator("[data-testid='add-application-source-field']");
    private ILocator RecruiterField => Dialog.Locator("[data-testid='add-application-recruiter-field']");

    private ILocator AttachCurrentCvWrapper => Dialog.Locator("[data-testid='attach-current-cv']");
    private ILocator AttachCurrentCvCheckbox => Dialog.Locator("#attach-current-cv-input");

    private ILocator CvInput => Dialog.Locator("input[type=file][data-testid='new-candidate-cv-input']");
    private ILocator CvSelected => Dialog.Locator("[data-testid='new-candidate-cv-selected']");
    private ILocator CvFileName => Dialog.Locator("[data-testid='new-candidate-cv-file-name']");
    private ILocator CvRemoveButton => Dialog.Locator("[data-testid='new-candidate-cv-remove']");
    private ILocator CvError => Dialog.Locator("[data-testid='new-candidate-cv-error']");

    private ILocator SubmitButton => Dialog.Locator("[data-testid='add-application-submit']");
    private ILocator CancelButton => Dialog.Locator("[data-testid='add-application-cancel']");
    private ILocator GeneralError => Dialog.Locator("[data-testid='add-application-error']");

    private ILocator DuplicateAlert => Dialog.Locator("[data-testid='duplicate-candidate-alert']");
    private ILocator DuplicateName => DuplicateAlert.Locator("[data-testid='duplicate-candidate-name']");
    private ILocator DuplicateEmail => DuplicateAlert.Locator("[data-testid='duplicate-candidate-email']");
    private ILocator SelectExistingButton => DuplicateAlert.Locator("[data-testid='select-existing-candidate-btn']");
    private ILocator DuplicateInactive => DuplicateAlert.Locator("[data-testid='duplicate-candidate-inactive']");
    private ILocator DuplicateDetailsLink => DuplicateAlert.Locator("[data-testid='duplicate-candidate-details-link']");

    private ILocator ValidationMessages => Dialog.Locator(".validation-message, [role='alert']");


    /// <summary>
    /// Switches to "Create new candidate" and waits for the new-candidate field group to render
    /// (the mode-specific fields are the reliable signal the server round trip landed — the mode
    /// button's own aria-pressed is deliberately not relied on).
    /// </summary>
    public async Task SwitchToNewModeAsync()
    {
        await ModeNewButton.ClickAsync();
        await Assertions.Expect(NewCandidateFields).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(CandidateField).ToBeHiddenAsync(new() { Timeout = 15_000 });
    }

    public async Task SwitchToExistingModeAsync()
    {
        await ModeExistingButton.ClickAsync();
        await ExpectExistingModeAsync();
    }

    public async Task ExpectExistingModeAsync()
    {
        await Assertions.Expect(CandidateField).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(NewCandidateFields).ToBeHiddenAsync(new() { Timeout = 15_000 });
    }


    public async Task SelectExistingCandidateAsync(string nameFragment)
    {
        await Assertions.Expect(CandidateField).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await DropDownSelector.SelectAsync(page, CandidateField, nameFragment);
    }

    public Task ExpectSelectedCandidateAsync(string nameFragment) =>
        Assertions.Expect(CandidateField.Locator("span[role='combobox'] input").First)
            .ToHaveValueAsync(new Regex(Regex.Escape(nameFragment)), new() { Timeout = 15_000 });

    public async Task ExpectAttachCurrentCvVisibleAsync(string expectedFileName)
    {
        await Assertions.Expect(AttachCurrentCvWrapper).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(AttachCurrentCvWrapper).ToContainTextAsync(expectedFileName, new() { Timeout = 15_000 });
    }

    public Task ExpectAttachCurrentCvCheckedAsync() =>
        Assertions.Expect(AttachCurrentCvCheckbox).ToBeCheckedAsync(new() { Timeout = 10_000 });


    private async Task FillAsync(string id, string value)
    {
        var input = NewCandidateFields.Locator($"input#{id}");
        await input.FillAsync(value);
        await input.PressAsync("Tab");
    }

    public Task FillFirstNameAsync(string value) => FillAsync("new-candidate-first-name", value);
    public Task FillLastNameAsync(string value) => FillAsync("new-candidate-last-name", value);
    public Task FillEmailAsync(string value) => FillAsync("new-candidate-email", value);
    public Task FillPhoneAsync(string value) => FillAsync("new-candidate-phone", value);

    public async Task FillNewCandidateAsync(string firstName, string lastName, string email)
    {
        await FillFirstNameAsync(firstName);
        await FillLastNameAsync(lastName);
        await FillEmailAsync(email);
    }

    public async Task SelectCvAsync(string fileName, byte[] content, string mimeType = "application/pdf")
    {
        await SetCvFileAsync(fileName, content, mimeType);
        await Assertions.Expect(CvSelected).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(CvFileName).ToHaveTextAsync(fileName, new() { Timeout = 15_000 });
    }

    /// <summary>Sets the CV file input without asserting acceptance (for rejected-file scenarios).</summary>
    public Task SetCvFileAsync(string fileName, byte[] content, string mimeType) =>
        CvInput.SetInputFilesAsync(new FilePayload { Name = fileName, MimeType = mimeType, Buffer = content });

    public async Task RemoveCvAsync()
    {
        await CvRemoveButton.ClickAsync();
        await Assertions.Expect(CvSelected).ToBeHiddenAsync(new() { Timeout = 15_000 });
    }

    public async Task ExpectCvErrorAsync(string textFragment)
    {
        await Assertions.Expect(CvError).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(CvError).ToContainTextAsync(textFragment);
    }

    public Task ExpectNoCvErrorAsync() =>
        Assertions.Expect(CvError).ToBeHiddenAsync(new() { Timeout = 15_000 });

    public Task ExpectNoCvSelectedAsync() =>
        Assertions.Expect(CvSelected).ToBeHiddenAsync(new() { Timeout = 15_000 });


    public async Task SelectSourceAsync(string sourceLabel)
    {
        await Assertions.Expect(SourceField).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await DropDownSelector.SelectAsync(page, SourceField, sourceLabel);
    }

    public async Task SelectRecruiterAsync(string agencyNameFragment)
    {
        await Assertions.Expect(RecruiterField).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await DropDownSelector.SelectAsync(page, RecruiterField, agencyNameFragment);
    }


    public async Task ClickSubmitAsync()
    {
        await Assertions.Expect(SubmitButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await SubmitButton.ClickAsync();
    }

    public async Task SubmitExpectingSuccessAsync()
    {
        await ClickSubmitAsync();
        await ExpectClosedAsync();
    }

    public Task ExpectClosedAsync() =>
        Assertions.Expect(Dialog).ToBeHiddenAsync(new() { Timeout = 30_000 });

    public Task ExpectOpenAsync() =>
        Assertions.Expect(Dialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

    public async Task CancelAsync()
    {
        await CancelButton.ClickAsync();
        await ExpectClosedAsync();
    }

    public async Task ExpectGeneralErrorAsync(string textFragment)
    {
        await Assertions.Expect(GeneralError).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(GeneralError).ToContainTextAsync(textFragment, new() { IgnoreCase = true });
    }

    public Task ExpectValidationMessageAsync(string text) =>
        Assertions.Expect(ValidationMessages.Filter(new() { HasText = text }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

    public Task ExpectNoValidationMessageAsync(string text) =>
        Assertions.Expect(ValidationMessages.Filter(new() { HasText = text }))
            .ToHaveCountAsync(0, new() { Timeout = 15_000 });


    public async Task ExpectDuplicateAlertAsync(string expectedName, string expectedEmail)
    {
        await Assertions.Expect(DuplicateAlert).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(DuplicateName).ToHaveTextAsync(expectedName);
        await Assertions.Expect(DuplicateEmail).ToHaveTextAsync(expectedEmail);
    }

    public Task ExpectDuplicateAlertHiddenAsync() =>
        Assertions.Expect(DuplicateAlert).ToBeHiddenAsync(new() { Timeout = 15_000 });

    public Task ExpectSelectExistingButtonVisibleAsync() =>
        Assertions.Expect(SelectExistingButton).ToBeVisibleAsync(new() { Timeout = 15_000 });

    public Task ExpectSelectExistingButtonAbsentAsync() =>
        Assertions.Expect(SelectExistingButton).ToHaveCountAsync(0, new() { Timeout = 15_000 });

    public async Task ClickSelectExistingCandidateAsync()
    {
        await SelectExistingButton.ClickAsync();
        await ExpectExistingModeAsync();
    }

    public async Task ExpectInactiveDuplicateAsync(Guid candidateId)
    {
        await Assertions.Expect(DuplicateInactive).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(DuplicateInactive).ToContainTextAsync("inactive");
        await Assertions.Expect(DuplicateDetailsLink).ToBeVisibleAsync();
        await Assertions.Expect(DuplicateDetailsLink)
            .ToHaveAttributeAsync("href", new Regex(Regex.Escape(candidateId.ToString()), RegexOptions.IgnoreCase));
    }
}
