using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed record InternalOfferTerms(
    string? Manager = null,
    DateOnly? StartDate = null,
    string Salary = "42000",
    string? Currency = null,
    DateOnly? ResponseDeadline = null,
    string? HoursPerWeek = null,
    string? Fte = null,
    string? Notes = null);

/// <summary>
/// Page object for the vacancy Applications tab's "Make an Offer" dialog (OfferCandidateDialog.razor,
/// also reopened by the "Revise Offer" toolbar item), including the extra fields shown for an INTERNAL
/// application. The dialog is located by role + accessible name; fields by their exact form label; the
/// Proposed Manager combobox is driven only through the shared DropDownSelector.
/// </summary>
public sealed class InternalOfferDialog(IPage page)
{
    public const string Title = "Make an Offer";

    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = Title });

    private ILocator Field(string label) =>
        Dialog.Locator(".col-md-6, .col-12").Filter(new()
        {
            Has = page.Locator("label.form-label", new()
            {
                HasTextRegex = new Regex($"^\\s*{Regex.Escape(label)}\\s*\\*?\\s*$"),
            }),
        });

    private ILocator InternalNotice => Dialog.Locator("[data-testid='offer-internal-notice']");
    private ILocator SalaryInput => Field("Offered Salary").Locator("input.e-numerictextbox, input.e-input").First;
    private ILocator StartDateInput => Field("Proposed Start Date").Locator("input.e-input").First;
    private ILocator OfferDateInput => Field("Offer Date").Locator("input.e-input").First;
    private ILocator DeadlineInput => Field("Respond By (optional)").Locator("input.e-input").First;
    private ILocator CurrencyInput => Field("Currency").Locator("input").First;
    private ILocator ManagerField => Field("Proposed Manager");
    private ILocator ManagerInput => ManagerField.Locator("span[role='combobox'] input").First;
    private ILocator HoursPerWeekInput => Field("Hours per Week").Locator("input.e-numerictextbox, input.e-input").First;
    private ILocator FteInput => Field("FTE").Locator("input.e-numerictextbox, input.e-input").First;
    private ILocator NotesInput => Dialog.Locator("textarea#offer-notes");
    private ILocator SubmitButton => Dialog.GetByRole(AriaRole.Button, new() { Name = "Make Offer", Exact = true });
    private ILocator ApplicationsAlert => page.Locator("[data-testid='vacancy-applications-tab'] .alert-success");

    public static string FormatUkDate(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public async Task WaitForOpenAsync()
    {
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await Dialog.Locator("[data-testid='offer-position-profile-context']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<bool> IsInternalAsync() => await InternalNotice.CountAsync() > 0;

    public Task ExpectInternalFieldsVisibleAsync(bool visible) =>
        visible
            ? Assertions.Expect(InternalNotice).ToBeVisibleAsync(new() { Timeout = 10_000 })
            : Assertions.Expect(InternalNotice).ToHaveCountAsync(0, new() { Timeout = 10_000 });

    public async Task ExpectInternalNoticeAsync()
    {
        await Assertions.Expect(InternalNotice).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(InternalNotice).ToContainTextAsync("This is an internal appointment");
        await Assertions.Expect(InternalNotice).ToContainTextAsync("must accept before they can be appointed");
    }

    public Task ExpectCurrencyAsync(string currency) =>
        Assertions.Expect(CurrencyInput).ToHaveValueAsync(currency, new() { Timeout = 10_000 });

    public Task ExpectManagerAsync(string text) =>
        Assertions.Expect(ManagerInput).ToHaveValueAsync(text, new() { Timeout = 10_000 });

    public Task ExpectProposedStartDateAsync(DateOnly date) =>
        Assertions.Expect(StartDateInput).ToHaveValueAsync(FormatUkDate(date), new() { Timeout = 10_000 });

    public Task ExpectSalaryContainsAsync(string formattedDigits) =>
        Assertions.Expect(SalaryInput).ToHaveValueAsync(new Regex(Regex.Escape(formattedDigits)), new() { Timeout = 10_000 });

    public Task SelectManagerAsync(string text) =>
        DropDownSelector.SelectAsync(page, ManagerField, text);

    public async Task FillAsync(InternalOfferTerms terms)
    {
        await FillNumericAsync(SalaryInput, terms.Salary);
        await Assertions.Expect(SalaryInput).ToHaveValueAsync(
            new Regex(Regex.Escape(decimal.Parse(terms.Salary, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.InvariantCulture))),
            new() { Timeout = 10_000 });

        await FillDateAsync(StartDateInput, terms.StartDate ?? DateOnly.FromDateTime(DateTime.Today));

        if (terms.ResponseDeadline is { } deadline)
            await FillDateAsync(DeadlineInput, deadline);

        if (terms.Currency is { } currency)
        {
            await CurrencyInput.ClickAsync();
            await CurrencyInput.FillAsync(currency);
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(CurrencyInput).ToHaveValueAsync(currency, new() { Timeout = 10_000 });
        }

        if (terms.Manager is { } manager)
            await SelectManagerAsync(manager);

        if (terms.HoursPerWeek is { } hours)
            await FillNumericAsync(HoursPerWeekInput, hours);

        if (terms.Fte is { } fte)
            await FillNumericAsync(FteInput, fte);

        if (terms.Notes is { } notes)
        {
            await NotesInput.FillAsync(notes);
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(NotesInput).ToHaveValueAsync(notes, new() { Timeout = 10_000 });
        }
    }

    public async Task SubmitExpectingSuccessAsync()
    {
        await SubmitButton.ClickAsync();
        await Assertions.Expect(Dialog).ToBeHiddenAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(ApplicationsAlert).ToHaveTextAsync("Offer made to candidate.", new() { Timeout = 15_000 });
    }

    public async Task SubmitExpectingValidationAsync(string expectedText)
    {
        await SubmitButton.ClickAsync();
        await Assertions.Expect(Dialog.GetByText(expectedText)).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(Dialog).ToBeVisibleAsync();
    }

    public async Task ClearStartDateAsync()
    {
        await StartDateInput.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(StartDateInput).ToHaveValueAsync("", new() { Timeout = 10_000 });
    }

    public async Task CancelAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Assertions.Expect(Dialog).ToBeHiddenAsync(new() { Timeout = 20_000 });
    }

    private async Task FillDateAsync(ILocator input, DateOnly date)
    {
        await input.ClickAsync();
        await input.FillAsync(FormatUkDate(date));
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(input).ToHaveValueAsync(FormatUkDate(date), new() { Timeout = 10_000 });
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
}
