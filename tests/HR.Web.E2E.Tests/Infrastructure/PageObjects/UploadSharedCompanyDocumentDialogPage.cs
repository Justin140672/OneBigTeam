using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Upload Document" flow on the Shared Documents list page
/// (/companies/{companyId}/shared-documents) and its UploadSharedCompanyDocumentDialog.razor
/// dialog — covering the Title/Category/File basics plus the "Requires employee acknowledgement"
/// toggle's auto-populate-from-company-default, live preview, and required-when-enabled
/// validation. Complements SharedDocumentDetailPage (which owns the detail-page/post-upload
/// flows) — this one is scoped to the list page's own upload entry point.
/// </summary>
public sealed class UploadSharedCompanyDocumentDialogPage(IPage page, string baseUrl)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });

    public async Task GoToListAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/shared-documents");
        await page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });
    }

    public async Task OpenAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsOpenAsync() => Dialog.IsVisibleAsync();

    public Task FillTitleAsync(string title) =>
        Dialog.GetByPlaceholder("Document title").FillAsync(title);

    /// <summary>
    /// Selects a category via the Syncfusion SfDropDownList (same click-open/wait-for-popup/
    /// click-item interaction pattern used throughout this test suite — see
    /// SharedDocumentDetailPage.EditTitleDescriptionCategoryAsync).
    /// </summary>
    public async Task SelectCategoryAsync(string categoryLabel)
    {
        var categoryGroup = Dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await categoryGroup.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = categoryLabel })
            .First
            .ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl", new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public Task SetFileAsync(string filePath) =>
        Dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

    private ILocator RequiresAcknowledgementCheckboxWrapper =>
        Dialog.Locator(".e-checkbox-wrapper").Filter(new() { HasText = "Requires employee acknowledgement" });

    public Task<bool> IsRequiresAcknowledgementCheckedAsync() =>
        RequiresAcknowledgementCheckboxWrapper.Locator("input[type='checkbox']").IsCheckedAsync();

    /// <summary>
    /// Toggles "Requires employee acknowledgement" on. When the statement field is currently
    /// blank, OnRequiresAcknowledgementChangedAsync auto-populates it from the company's
    /// GetCompanySettingsAsync().DefaultAcknowledgementStatement — an async round trip, so this
    /// waits briefly for the statement textarea to actually appear with content rather than
    /// asserting immediately after the click.
    /// </summary>
    public async Task CheckRequiresAcknowledgementAsync()
    {
        await RequiresAcknowledgementCheckboxWrapper.Locator("label").ClickAsync();
        await Dialog.Locator("textarea").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    private ILocator AcknowledgementDueDateInput =>
        Dialog.Locator(".col-md-6").Filter(new() { HasText = "Acknowledgement Due Date" }).Locator(".e-date-wrapper input.e-input");

    public async Task FillAcknowledgementDueDateAsync(DateOnly dueDate)
    {
        await AcknowledgementDueDateInput.ClickAsync();
        await AcknowledgementDueDateInput.FillAsync(dueDate.ToString("dd/MM/yyyy"));
        await page.Keyboard.PressAsync("Tab");
    }

    // Description (above, in the Details section) is also an HrTextBox with Multiline="true", so
    // an unscoped page-wide "textarea" locator would resolve to two elements once "Requires
    // employee acknowledgement" is on — scope to the "Acknowledgement Statement" field's own
    // ".col-12" group to disambiguate.
    private ILocator AcknowledgementStatementTextArea =>
        Dialog.Locator(".col-12").Filter(new() { HasText = "Acknowledgement Statement" }).Locator("textarea");

    public Task<string> GetAcknowledgementStatementValueAsync() =>
        AcknowledgementStatementTextArea.InputValueAsync();

    public Task FillAcknowledgementStatementAsync(string value) =>
        AcknowledgementStatementTextArea.FillAsync(value);

    /// <summary>
    /// Text of the live "Preview — this is what employees will see:" box (.alert-info), which
    /// UploadSharedCompanyDocumentDialog.razor keeps in sync with Model.AcknowledgementStatement
    /// as the field is edited.
    /// </summary>
    public async Task<string> GetAcknowledgementPreviewTextAsync() =>
        (await Dialog.Locator(".alert-info").InnerTextAsync()).Trim();

    /// <summary>
    /// The inline "An acknowledgement statement is required." message, only rendered once the
    /// field has been touched (_statementTouched) and is currently blank — i.e. after a blocked
    /// save attempt, not merely from checking the acknowledgement box. Null if not shown.
    /// </summary>
    public async Task<string?> GetAcknowledgementStatementValidationErrorAsync()
    {
        var error = Dialog.Locator(".text-danger.small").Filter(new() { HasText = "acknowledgement statement is required" });
        if (!await error.IsVisibleAsync()) return null;
        return (await error.InnerTextAsync()).Trim();
    }

    /// <summary>The dialog-level error banner (GlobalError, e.g. "Please select a file." from ValidateExtra), or null if none is shown.</summary>
    public async Task<string?> GetGlobalErrorAsync()
    {
        var error = Dialog.Locator(".alert-danger");
        if (!await error.IsVisibleAsync()) return null;
        return (await error.InnerTextAsync()).Trim();
    }

    /// <summary>Clicks "Upload" without waiting for the dialog to close — for exercising validation paths that keep it open.</summary>
    public Task ClickUploadAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

    /// <summary>
    /// Clicks "Upload" and waits for a successful save to close the dialog. Assumes the form is
    /// valid — use <see cref="ClickUploadAsync"/> directly to exercise a blocked-save validation
    /// path instead.
    /// </summary>
    public async Task UploadAndWaitForCloseAsync()
    {
        await ClickUploadAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    /// <summary>
    /// Resolves the newly uploaded document's Id from the list grid row matching
    /// <paramref name="title"/> (its row link's href ends with the document's Guid) — same
    /// approach as CompanyDocumentsTabTests.UploadAndPublishDocumentAsync. Assumes the dialog has
    /// already closed after a successful upload and the grid has refreshed to show the new row.
    /// </summary>
    public async Task<Guid> GetUploadedDocumentIdAsync(string title)
    {
        await page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
        var href = await page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.GetAttributeAsync("href");
        return Guid.Parse(href!.Split('/').Last());
    }
}
