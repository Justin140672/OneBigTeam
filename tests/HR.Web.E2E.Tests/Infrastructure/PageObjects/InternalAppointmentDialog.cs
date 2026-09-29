using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the vacancy Applications tab's "Complete internal appointment" dialog
/// (internal recruitment Ticket 7 — VacancyApplicationsTab.razor, SfDialog CssClass
/// "appoint-candidate-dialog", content wrapper data-testid="appoint-candidate-dialog") and the
/// success banner it leaves behind on the tab (data-testid="appoint-success").
///
/// Locator notes:
///  - The dialog is located by role + accessible name (its Header), never by the bare
///    "appoint-candidate-dialog" CssClass, which Syncfusion also stamps on the modal container and
///    footer buttons (strict-mode ambiguity).
///  - Fields are located inside the content wrapper by their exact form label (Syncfusion inputs
///    don't reliably carry the component's data-testid onto the &lt;input&gt; itself); read-only
///    values and wrappers that DO render their own element use their data-testid directly.
///  - The Manager / Salary Type comboboxes are driven only through the shared DropDownSelector.
///  - Checkboxes (SfCheckBox) are driven through their native input inside the testid wrapper —
///    the same Check/UncheckAsync approach VacancyDetailPage uses for its SfCheckBoxes.
/// </summary>
public sealed class InternalAppointmentDialog(IPage page)
{
    public const string Title = "Complete internal appointment";

    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = Title });

    private ILocator Content => Dialog.Locator("[data-testid='appoint-candidate-dialog']");

    private ILocator ApplicationsTab => page.Locator("[data-testid='vacancy-applications-tab']");

    private ILocator Field(string label) =>
        Content.Locator(".col-md-4, .col-md-6, .col-12").Filter(new()
        {
            Has = page.Locator("label.form-label", new()
            {
                HasTextRegex = new Regex($"^\\s*{Regex.Escape(label)}\\s*\\*?\\s*$"),
            }),
        });

    private ILocator Notice => Content.Locator("[data-testid='appoint-existing-employee-notice']");
    private ILocator DerivedPositionProfile => Content.Locator("[data-testid='appoint-derived-position-profile']");
    private ILocator DerivedDepartment => Content.Locator("[data-testid='appoint-derived-department']");
    private ILocator DerivedLocation => Content.Locator("[data-testid='appoint-derived-location']");
    private ILocator FutureDateHint => Content.Locator("[data-testid='appoint-future-date-hint']");
    private ILocator ConfirmBackdatedWrapper => Content.Locator("[data-testid='appoint-confirm-backdated']");
    private ILocator ChangeCompensationWrapper => Content.Locator("[data-testid='appoint-change-compensation']");
    private ILocator Error => Content.Locator("[data-testid='appoint-error']");

    private ILocator EffectiveDateInput => Field("Effective Date").Locator("input.e-input").First;
    private ILocator ManagerField => Field("Manager");
    private ILocator ManagerInput => ManagerField.Locator("span[role='combobox'] input").First;

    private ILocator SalaryTypeField => Field("Salary Type");
    private ILocator SalaryInput => Field("Salary").Locator("input.e-numerictextbox, input.e-input").First;
    private ILocator CurrencyInput => Field("Currency").Locator("input").First;
    private ILocator HoursPerWeekInput => Field("Hours per Week").Locator("input.e-numerictextbox, input.e-input").First;
    private ILocator FteInput => Field("FTE").Locator("input.e-numerictextbox, input.e-input").First;
    private ILocator CompensationNotesInput => Field("Compensation Notes").Locator("textarea").First;

    private ILocator ConfirmButton => Dialog.GetByRole(AriaRole.Button, new() { Name = "Appoint", Exact = true });
    private ILocator CancelButton => Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    public static string FormatUkDate(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);


    public async Task WaitForOpenAsync()
    {
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await Assertions.Expect(DerivedPositionProfile).Not.ToBeEmptyAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(ManagerField.Locator("span[role='combobox']").First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    public async Task CancelAsync()
    {
        await CancelButton.ClickAsync();
        await ExpectClosedAsync();
    }

    public Task ExpectClosedAsync() =>
        Assertions.Expect(Dialog).ToBeHiddenAsync(new() { Timeout = 20_000 });

    public Task ExpectOpenAsync() =>
        Assertions.Expect(Dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });


    public async Task ExpectExistingEmployeeNoticeAsync(string employeeFullName)
    {
        await Assertions.Expect(Notice).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(Notice).ToContainTextAsync($"{employeeFullName} is an existing employee");
        await Assertions.Expect(Notice).ToContainTextAsync("no new employee will be created");
    }

    public async Task ExpectDerivedFieldsAsync(string positionProfile, string department, string location)
    {
        await Assertions.Expect(DerivedPositionProfile).ToHaveTextAsync(positionProfile, new() { Timeout = 10_000 });
        await Assertions.Expect(DerivedDepartment).ToHaveTextAsync(department, new() { Timeout = 10_000 });
        await Assertions.Expect(DerivedLocation).ToHaveTextAsync(location, new() { Timeout = 10_000 });
    }

    public Task ExpectOnlyManagerComboboxAsync() =>
        Assertions.Expect(Content.Locator("span[role='combobox']")).ToHaveCountAsync(1, new() { Timeout = 10_000 });


    public Task ExpectEffectiveDateAsync(DateOnly date) =>
        Assertions.Expect(EffectiveDateInput).ToHaveValueAsync(FormatUkDate(date), new() { Timeout = 10_000 });

    public async Task SetEffectiveDateAsync(DateOnly date)
    {
        await EffectiveDateInput.ClickAsync();
        await EffectiveDateInput.FillAsync(FormatUkDate(date));
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(EffectiveDateInput).ToHaveValueAsync(FormatUkDate(date), new() { Timeout = 10_000 });
    }

    public Task ExpectFutureDateHintAsync(bool visible) =>
        visible
            ? Assertions.Expect(FutureDateHint).ToBeVisibleAsync(new() { Timeout = 10_000 })
            : Assertions.Expect(FutureDateHint).ToHaveCountAsync(0, new() { Timeout = 10_000 });

    public Task ExpectBackdatedConfirmationAsync(bool visible) =>
        visible
            ? Assertions.Expect(ConfirmBackdatedWrapper).ToBeVisibleAsync(new() { Timeout = 10_000 })
            : Assertions.Expect(ConfirmBackdatedWrapper).ToHaveCountAsync(0, new() { Timeout = 10_000 });

    public async Task ConfirmBackdatedAsync()
    {
        var checkbox = ConfirmBackdatedWrapper.Locator("input[type='checkbox']").First;
        await checkbox.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        await checkbox.CheckAsync();
        await Assertions.Expect(checkbox).ToBeCheckedAsync(new() { Timeout = 5_000 });
    }


    public Task ExpectManagerAsync(string text) =>
        Assertions.Expect(ManagerInput).ToHaveValueAsync(text, new() { Timeout = 10_000 });

    public Task SelectManagerAsync(string text) =>
        DropDownSelector.SelectAsync(page, ManagerField, text);


    public async Task SetChangeCompensationAsync(bool value)
    {
        var checkbox = ChangeCompensationWrapper.Locator("input[type='checkbox']").First;
        await checkbox.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        if (value)
        {
            await checkbox.CheckAsync();
            await Assertions.Expect(SalaryInput).ToBeVisibleAsync(new() { Timeout = 10_000 });
        }
        else
        {
            await checkbox.UncheckAsync();
            await Assertions.Expect(Field("Salary")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        }
    }

    public async Task ExpectCompensationFieldsVisibleAsync()
    {
        await Assertions.Expect(SalaryTypeField.Locator("span[role='combobox']").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(SalaryTypeField.Locator("span[role='combobox'] input").First).ToHaveValueAsync("Annual");
        await Assertions.Expect(SalaryInput).ToBeVisibleAsync();
        await Assertions.Expect(CurrencyInput).ToBeVisibleAsync();
        await Assertions.Expect(CurrencyInput).ToHaveValueAsync("GBP");
        await Assertions.Expect(HoursPerWeekInput).ToBeVisibleAsync();
        await Assertions.Expect(FteInput).ToBeVisibleAsync();
        await Assertions.Expect(CompensationNotesInput).ToBeVisibleAsync();
    }

    public Task ExpectCompensationFieldsHiddenAsync() =>
        Assertions.Expect(Field("Salary")).ToHaveCountAsync(0, new() { Timeout = 10_000 });

    public Task SelectSalaryTypeAsync(string salaryType) =>
        DropDownSelector.SelectAsync(page, SalaryTypeField, salaryType);

    public Task FillSalaryAsync(string value) => FillNumericAsync(SalaryInput, value);

    public Task FillHoursPerWeekAsync(string value) => FillNumericAsync(HoursPerWeekInput, value);

    public async Task FillCompensationNotesAsync(string value)
    {
        await CompensationNotesInput.ClickAsync();
        await CompensationNotesInput.PressSequentiallyAsync(value, new() { Delay = 10 });
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(CompensationNotesInput).ToHaveValueAsync(value, new() { Timeout = 10_000 });
    }

    private async Task FillNumericAsync(ILocator input, string value)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await input.PressSequentiallyAsync(value, new() { Delay = 10 });
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(input).Not.ToHaveValueAsync("", new() { Timeout = 10_000 });
    }


    public async Task SubmitExpectingSuccessAsync()
    {
        await ConfirmButton.ClickAsync();
        await ExpectClosedAsync();
    }

    public async Task SubmitExpectingErrorAsync(string expectedText)
    {
        await ConfirmButton.ClickAsync();
        await Assertions.Expect(Error).ToContainTextAsync(expectedText, new() { Timeout = 15_000 });
        await ExpectOpenAsync();
    }


    private ILocator SuccessBanner => ApplicationsTab.Locator("[data-testid='appoint-success']");
    private ILocator EmployeeLink => SuccessBanner.Locator("[data-testid='appoint-employee-link']");

    public async Task ExpectAppliedSuccessBannerAsync()
    {
        await Assertions.Expect(SuccessBanner).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(SuccessBanner).ToContainTextAsync("Internal appointment completed.");
        await Assertions.Expect(SuccessBanner).Not.ToContainTextAsync("takes effect");
    }

    public async Task ExpectScheduledSuccessBannerAsync(DateOnly effectiveDate)
    {
        await Assertions.Expect(SuccessBanner).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(SuccessBanner).ToContainTextAsync(
            $"Internal appointment completed — the change takes effect on {FormatUkDate(effectiveDate)}.");
    }

    public Task ExpectNoSuccessBannerAsync() =>
        Assertions.Expect(SuccessBanner).ToHaveCountAsync(0, new() { Timeout = 10_000 });

    public async Task ExpectEmployeeNameWithoutLinkAsync(string employeeFullName)
    {
        await Assertions.Expect(SuccessBanner.Locator("[data-testid='appoint-employee-name']"))
            .ToHaveTextAsync(employeeFullName, new() { Timeout = 10_000 });
        await Assertions.Expect(EmployeeLink).ToHaveCountAsync(0);
    }

    public Task ExpectEmployeeLinkAsync(Guid companyId, Guid employeeId) =>
        Assertions.Expect(EmployeeLink).ToHaveAttributeAsync(
            "href", $"/companies/{companyId}/employees/{employeeId}", new() { Timeout = 10_000 });

    public async Task FollowEmployeeLinkAsync(Guid employeeId)
    {
        await EmployeeLink.ClickAsync();
        await page.WaitForURLAsync(
            new Regex($"/employees/{employeeId}(/view)?([?#].*)?$", RegexOptions.IgnoreCase),
            new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });
    }
}
